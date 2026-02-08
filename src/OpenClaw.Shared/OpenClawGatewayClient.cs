using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OpenClaw.Shared;

public class OpenClawGatewayClient : IDisposable
{
    private ClientWebSocket? _webSocket;
    private readonly string _gatewayUrl;
    private readonly string _token;
    private readonly IOpenClawLogger _logger;
    private CancellationTokenSource _cts;
    private bool _disposed;
    private int _reconnectAttempts;
    private static readonly int[] BackoffMs = { 1000, 2000, 4000, 8000, 15000, 30000, 60000 };

    // Tracked state
    private readonly Dictionary<string, SessionInfo> _sessions = new();
    private GatewayUsageInfo? _usage;

    // Events
    public event EventHandler<ConnectionStatus>? StatusChanged;
    public event EventHandler<OpenClawNotification>? NotificationReceived;
    public event EventHandler<AgentActivity>? ActivityChanged;
    public event EventHandler<ChannelHealth[]>? ChannelHealthUpdated;
    public event EventHandler<SessionInfo[]>? SessionsUpdated;
    public event EventHandler<GatewayUsageInfo>? UsageUpdated;

    public OpenClawGatewayClient(string gatewayUrl, string token, IOpenClawLogger? logger = null)
    {
        _gatewayUrl = gatewayUrl;
        _token = token;
        _logger = logger ?? NullLogger.Instance;
        _cts = new CancellationTokenSource();
    }

    public async Task ConnectAsync()
    {
        try
        {
            StatusChanged?.Invoke(this, ConnectionStatus.Connecting);
            _logger.Info($"Connecting to gateway: {_gatewayUrl}");

            _webSocket = new ClientWebSocket();
            _webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
            
            // Set Origin header based on gateway URL (convert ws/wss to http/https)
            var uri = new Uri(_gatewayUrl);
            var originScheme = uri.Scheme == "wss" ? "https" : "http";
            var origin = $"{originScheme}://{uri.Host}:{uri.Port}";
            _webSocket.Options.SetRequestHeader("Origin", origin);
            
            await _webSocket.ConnectAsync(uri, _cts.Token);

            _reconnectAttempts = 0;
            _logger.Info("Gateway connected, waiting for challenge...");

            // Don't send connect yet - wait for challenge event in ListenForMessagesAsync
            _ = Task.Run(() => ListenForMessagesAsync(), _cts.Token);
        }
        catch (Exception ex)
        {
            _logger.Error("Connection failed", ex);
            StatusChanged?.Invoke(this, ConnectionStatus.Error);
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        if (_webSocket?.State == WebSocketState.Open)
        {
            try
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnecting", CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Error during disconnect: {ex.Message}");
            }
        }
        StatusChanged?.Invoke(this, ConnectionStatus.Disconnected);
        _logger.Info("Disconnected");
    }

    public async Task CheckHealthAsync()
    {
        if (_webSocket?.State != WebSocketState.Open)
        {
            await ReconnectWithBackoffAsync();
            return;
        }

        try
        {
            var req = new
            {
                type = "req",
                id = Guid.NewGuid().ToString(),
                method = "health",
                @params = new { deep = true }
            };
            await SendRawAsync(JsonSerializer.Serialize(req));
        }
        catch (Exception ex)
        {
            _logger.Error("Health check failed", ex);
            StatusChanged?.Invoke(this, ConnectionStatus.Error);
            await ReconnectWithBackoffAsync();
        }
    }

    public async Task SendChatMessageAsync(string message)
    {
        if (_webSocket?.State != WebSocketState.Open)
            throw new InvalidOperationException("Gateway connection is not open");

        var req = new
        {
            type = "req",
            id = Guid.NewGuid().ToString(),
            method = "chat.send",
            @params = new { message }
        };
        await SendRawAsync(JsonSerializer.Serialize(req));
        _logger.Info($"Sent chat message ({message.Length} chars)");
    }

    /// <summary>Request session list from gateway.</summary>
    public async Task RequestSessionsAsync()
    {
        if (_webSocket?.State != WebSocketState.Open) return;
        var req = new
        {
            type = "req",
            id = Guid.NewGuid().ToString(),
            method = "sessions.list"
        };
        await SendRawAsync(JsonSerializer.Serialize(req));
    }

    /// <summary>Request usage/context info from gateway (may not be supported on all gateways).</summary>
    public async Task RequestUsageAsync()
    {
        // Usage endpoint may not exist on all gateways - fail silently
        if (_webSocket?.State != WebSocketState.Open) return;
        try
        {
            var req = new
            {
                type = "req",
                id = Guid.NewGuid().ToString(),
                method = "usage"
            };
            await SendRawAsync(JsonSerializer.Serialize(req));
        }
        catch { }
    }

    /// <summary>Start a channel (telegram, whatsapp, etc).</summary>
    public async Task<bool> StartChannelAsync(string channelName)
    {
        if (_webSocket?.State != WebSocketState.Open) return false;
        try
        {
            var req = new
            {
                type = "req",
                id = Guid.NewGuid().ToString(),
                method = "channel.start",
                @params = new { channel = channelName }
            };
            await SendRawAsync(JsonSerializer.Serialize(req));
            _logger.Info($"Sent channel.start for {channelName}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to start channel {channelName}", ex);
            return false;
        }
    }

    /// <summary>Stop a channel (telegram, whatsapp, etc).</summary>
    public async Task<bool> StopChannelAsync(string channelName)
    {
        if (_webSocket?.State != WebSocketState.Open) return false;
        try
        {
            var req = new
            {
                type = "req",
                id = Guid.NewGuid().ToString(),
                method = "channel.stop",
                @params = new { channel = channelName }
            };
            await SendRawAsync(JsonSerializer.Serialize(req));
            _logger.Info($"Sent channel.stop for {channelName}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to stop channel {channelName}", ex);
            return false;
        }
    }

    // --- Connection management ---

    private async Task ReconnectWithBackoffAsync()
    {
        var delay = BackoffMs[Math.Min(_reconnectAttempts, BackoffMs.Length - 1)];
        _reconnectAttempts++;
        _logger.Warn($"Reconnecting in {delay}ms (attempt {_reconnectAttempts})");
        StatusChanged?.Invoke(this, ConnectionStatus.Connecting);

        try
        {
            await Task.Delay(delay, _cts.Token);
            _webSocket?.Dispose();
            _webSocket = null;
            await ConnectAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.Error("Reconnect failed", ex);
            StatusChanged?.Invoke(this, ConnectionStatus.Error);
            // Don't recurse — the listen loop will trigger reconnect again
        }
    }

    private async Task SendConnectMessageAsync(string? nonce = null)
    {
        // Use "cli" client ID for native apps - no browser security checks
        var msg = new
        {
            type = "req",
            id = Guid.NewGuid().ToString(),
            method = "connect",
            @params = new
            {
                minProtocol = 3,
                maxProtocol = 3,
                client = new
                {
                    id = "cli",  // Native client ID
                    version = "1.0.0",
                    platform = "windows",
                    mode = "cli",
                    displayName = "OpenClaw Windows Tray"
                },
                role = "operator",
                scopes = new[] { "operator.admin", "operator.approvals", "operator.pairing" },
                caps = Array.Empty<string>(),
                commands = Array.Empty<string>(),
                permissions = new { },
                auth = new { token = _token },
                locale = "en-US",
                userAgent = "openclaw-windows-tray/1.0.0"
            }
        };
        await SendRawAsync(JsonSerializer.Serialize(msg));
    }

    private async Task SendRawAsync(string message)
    {
        if (_webSocket?.State == WebSocketState.Open)
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            await _webSocket.SendAsync(new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text, true, _cts.Token);
        }
    }

    // --- Message loop ---

    private async Task ListenForMessagesAsync()
    {
        var buffer = new byte[16384]; // Larger buffer for big events
        var sb = new StringBuilder();

        try
        {
            while (_webSocket?.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
            {
                var result = await _webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer), _cts.Token);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    if (result.EndOfMessage)
                    {
                        ProcessMessage(sb.ToString());
                        sb.Clear();
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    var closeStatus = _webSocket.CloseStatus?.ToString() ?? "unknown";
                    var closeDesc = _webSocket.CloseStatusDescription ?? "no description";
                    _logger.Info($"Server closed connection: {closeStatus} - {closeDesc}");
                    StatusChanged?.Invoke(this, ConnectionStatus.Disconnected);
                    break;
                }
            }
        }
        catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
        {
            _logger.Warn("Connection closed prematurely");
            StatusChanged?.Invoke(this, ConnectionStatus.Disconnected);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.Error("Listen error", ex);
            StatusChanged?.Invoke(this, ConnectionStatus.Error);
        }

        // Auto-reconnect if not intentionally disposed
        if (!_disposed && !_cts.Token.IsCancellationRequested)
        {
            await ReconnectWithBackoffAsync();
        }
    }

    // --- Message processing ---

    private void ProcessMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeProp)) return;
            var type = typeProp.GetString();

            switch (type)
            {
                case "res":
                    HandleResponse(root);
                    break;
                case "event":
                    HandleEvent(root);
                    break;
            }
        }
        catch (JsonException ex)
        {
            _logger.Warn($"JSON parse error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.Error("Message processing error", ex);
        }
    }

    private void HandleResponse(JsonElement root)
    {
        if (!root.TryGetProperty("payload", out var payload)) return;

        // Handle hello-ok
        if (payload.TryGetProperty("type", out var t) && t.GetString() == "hello-ok")
        {
            _logger.Info("Handshake complete (hello-ok)");
            StatusChanged?.Invoke(this, ConnectionStatus.Connected);

            // Request initial state after handshake
            _ = Task.Run(async () =>
            {
                await Task.Delay(500);
                await CheckHealthAsync();
                await RequestSessionsAsync();
                await RequestUsageAsync();
            });
        }

        // Handle health response — channels
        if (payload.TryGetProperty("channels", out var channels))
        {
            ParseChannelHealth(channels);
        }

        // Handle sessions response
        if (payload.TryGetProperty("sessions", out var sessions))
        {
            ParseSessions(sessions);
        }

        // Handle usage response
        if (payload.TryGetProperty("usage", out var usage))
        {
            ParseUsage(usage);
        }
    }

    private void HandleEvent(JsonElement root)
    {
        if (!root.TryGetProperty("event", out var eventProp)) return;
        var eventType = eventProp.GetString();

        switch (eventType)
        {
            case "connect.challenge":
                HandleConnectChallenge(root);
                break;
            case "agent":
                HandleAgentEvent(root);
                break;
            case "health":
                if (root.TryGetProperty("payload", out var hp) &&
                    hp.TryGetProperty("channels", out var ch))
                    ParseChannelHealth(ch);
                break;
            case "chat":
                HandleChatEvent(root);
                break;
            case "session":
                HandleSessionEvent(root);
                break;
        }
    }

    private void HandleConnectChallenge(JsonElement root)
    {
        string? nonce = null;
        if (root.TryGetProperty("payload", out var payload) &&
            payload.TryGetProperty("nonce", out var nonceProp))
        {
            nonce = nonceProp.GetString();
        }
        
        _logger.Info($"Received challenge, nonce: {nonce}");
        _ = SendConnectMessageAsync(nonce);
    }

    private void HandleAgentEvent(JsonElement root)
    {
        if (!root.TryGetProperty("payload", out var payload)) return;

        // Determine session
        var sessionKey = "unknown";
        if (root.TryGetProperty("sessionKey", out var sk))
            sessionKey = sk.GetString() ?? "unknown";
        var isMain = sessionKey == "main" || sessionKey.Contains(":main:");

        // Parse activity from stream field
        if (payload.TryGetProperty("stream", out var streamProp))
        {
            var stream = streamProp.GetString();

            if (stream == "job")
            {
                HandleJobEvent(payload, sessionKey, isMain);
            }
            else if (stream == "tool")
            {
                HandleToolEvent(payload, sessionKey, isMain);
            }
        }

        // Check for notification content
        if (payload.TryGetProperty("content", out var content))
        {
            var text = content.GetString() ?? "";
            if (!string.IsNullOrEmpty(text))
            {
                EmitNotification(text);
            }
        }
    }

    private void HandleJobEvent(JsonElement payload, string sessionKey, bool isMain)
    {
        var state = "unknown";
        if (payload.TryGetProperty("data", out var data) &&
            data.TryGetProperty("state", out var stateProp))
            state = stateProp.GetString() ?? "unknown";

        var activity = new AgentActivity
        {
            SessionKey = sessionKey,
            IsMain = isMain,
            Kind = ActivityKind.Job,
            State = state,
            Label = $"Job: {state}"
        };

        if (state == "done" || state == "error")
            activity.Kind = ActivityKind.Idle;

        _logger.Info($"Agent activity: {activity.Label} (session: {sessionKey})");
        ActivityChanged?.Invoke(this, activity);

        // Update tracked session
        UpdateTrackedSession(sessionKey, isMain, state == "done" || state == "error" ? null : $"Job: {state}");
    }

    private void HandleToolEvent(JsonElement payload, string sessionKey, bool isMain)
    {
        var phase = "";
        var toolName = "";
        var label = "";

        if (payload.TryGetProperty("data", out var data))
        {
            if (data.TryGetProperty("phase", out var phaseProp))
                phase = phaseProp.GetString() ?? "";
            if (data.TryGetProperty("name", out var nameProp))
                toolName = nameProp.GetString() ?? "";

            // Extract detail from args
            if (data.TryGetProperty("args", out var args))
            {
                if (args.TryGetProperty("command", out var cmd))
                    label = TruncateLabel(cmd.GetString()?.Split('\n')[0] ?? "");
                else if (args.TryGetProperty("path", out var path))
                    label = ShortenPath(path.GetString() ?? "");
                else if (args.TryGetProperty("file_path", out var filePath))
                    label = ShortenPath(filePath.GetString() ?? "");
                else if (args.TryGetProperty("query", out var query))
                    label = TruncateLabel(query.GetString() ?? "");
                else if (args.TryGetProperty("url", out var url))
                    label = TruncateLabel(url.GetString() ?? "");
            }
        }

        if (string.IsNullOrEmpty(label))
            label = toolName;

        var kind = ClassifyTool(toolName);

        // On tool result, briefly show then go idle
        if (phase == "result")
            kind = ActivityKind.Idle;

        var activity = new AgentActivity
        {
            SessionKey = sessionKey,
            IsMain = isMain,
            Kind = kind,
            State = phase,
            ToolName = toolName,
            Label = label
        };

        _logger.Info($"Tool: {toolName} ({phase}) — {label}");
        ActivityChanged?.Invoke(this, activity);

        // Update tracked session
        if (kind != ActivityKind.Idle)
        {
            UpdateTrackedSession(sessionKey, isMain, $"{activity.Glyph} {label}");
        }
    }

    private void HandleChatEvent(JsonElement root)
    {
        _logger.Info($"Chat event received: {root.GetRawText().Substring(0, Math.Min(200, root.GetRawText().Length))}");
        
        if (!root.TryGetProperty("payload", out var payload)) return;

        // Try new format: payload.message.role + payload.message.content[].text
        if (payload.TryGetProperty("message", out var message))
        {
            if (message.TryGetProperty("role", out var role) && role.GetString() == "assistant")
            {
                // Extract text from content array
                if (message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in content.EnumerateArray())
                    {
                        if (item.TryGetProperty("type", out var type) && type.GetString() == "text" &&
                            item.TryGetProperty("text", out var textProp))
                        {
                            var text = textProp.GetString() ?? "";
                            if (!string.IsNullOrEmpty(text) && 
                                payload.TryGetProperty("state", out var state) && 
                                state.GetString() == "final")
                            {
                                _logger.Info($"Assistant response: {text.Substring(0, Math.Min(100, text.Length))}...");
                                EmitChatNotification(text);
                            }
                        }
                    }
                }
            }
        }
        
        // Legacy format: payload.text + payload.role
        else if (payload.TryGetProperty("text", out var textProp))
        {
            var text = textProp.GetString() ?? "";
            if (payload.TryGetProperty("role", out var role) &&
                role.GetString() == "assistant" &&
                !string.IsNullOrEmpty(text))
            {
                _logger.Info($"Assistant response (legacy): {text.Substring(0, Math.Min(100, text.Length))}");
                EmitChatNotification(text);
            }
        }
    }

    private void EmitChatNotification(string text)
    {
        var displayText = text.Length > 200 ? text[..200] + "…" : text;
        var notification = new OpenClawNotification
        {
            Message = displayText,
            IsChat = true
        };
        var (title, type) = _categorizer.Classify(notification);
        notification.Title = title;
        notification.Type = type;
        NotificationReceived?.Invoke(this, notification);
    }

    private void HandleSessionEvent(JsonElement root)
    {
        // Re-request sessions list when session events come through
        _ = RequestSessionsAsync();
    }

    // --- State tracking ---

    private void UpdateTrackedSession(string sessionKey, bool isMain, string? currentActivity)
    {
        if (!_sessions.ContainsKey(sessionKey))
        {
            _sessions[sessionKey] = new SessionInfo
            {
                Key = sessionKey,
                IsMain = isMain,
                Status = "active"
            };
        }

        _sessions[sessionKey].CurrentActivity = currentActivity;
        _sessions[sessionKey].LastSeen = DateTime.UtcNow;

        SessionsUpdated?.Invoke(this, GetSessionList());
    }

    public SessionInfo[] GetSessionList()
    {
        var list = new List<SessionInfo>(_sessions.Values);
        list.Sort((a, b) =>
        {
            // Main session first, then by last seen
            if (a.IsMain != b.IsMain) return a.IsMain ? -1 : 1;
            return b.LastSeen.CompareTo(a.LastSeen);
        });
        return list.ToArray();
    }

    // --- Parsing helpers ---

    private void ParseChannelHealth(JsonElement channels)
    {
        var healthList = new List<ChannelHealth>();
        
        // Debug: log raw channel data
        _logger.Info($"Raw channel health JSON: {channels.GetRawText()}");

        foreach (var prop in channels.EnumerateObject())
        {
            var ch = new ChannelHealth { Name = prop.Name };
            var val = prop.Value;

            // Get running status
            bool isRunning = false;
            bool isConfigured = false;
            bool isLinked = false;
            bool probeOk = false;
            bool hasError = false;
            string? tokenSource = null;
            
            if (val.TryGetProperty("running", out var running))
                isRunning = running.GetBoolean();
            if (val.TryGetProperty("configured", out var configured))
                isConfigured = configured.GetBoolean();
            if (val.TryGetProperty("linked", out var linked))
            {
                isLinked = linked.GetBoolean();
                ch.IsLinked = isLinked;
            }
            // Check probe status for webhook-based channels like Telegram
            if (val.TryGetProperty("probe", out var probe) && probe.TryGetProperty("ok", out var ok))
                probeOk = ok.GetBoolean();
            // Check for errors
            if (val.TryGetProperty("lastError", out var lastError) && lastError.ValueKind != JsonValueKind.Null)
                hasError = true;
            // Check token source (for Telegram - if configured, bot token was validated)
            if (val.TryGetProperty("tokenSource", out var ts))
                tokenSource = ts.GetString();
            
            // Determine status string - unified for parity between channels
            // Key insight: if configured=true and no errors, the channel is ready
            // - WhatsApp: linked=true means authenticated
            // - Telegram: configured=true means bot token was validated
            if (val.TryGetProperty("status", out var status))
                ch.Status = status.GetString() ?? "unknown";
            else if (hasError)
                ch.Status = "error";
            else if (isRunning)
                ch.Status = "running";
            else if (isConfigured && (probeOk || isLinked))
                ch.Status = "ready";  // Explicitly verified ready
            else if (isConfigured && !hasError)
                ch.Status = "ready";  // Configured without errors = ready (token was validated at config time)
            else
                ch.Status = "not configured";
            
            if (val.TryGetProperty("error", out var error))
                ch.Error = error.GetString();
            if (val.TryGetProperty("authAge", out var authAge))
                ch.AuthAge = authAge.GetString();
            if (val.TryGetProperty("type", out var chType))
                ch.Type = chType.GetString();

            healthList.Add(ch);
        }

        if (healthList.Count > 0)
        {
            _logger.Info($"Channel health: {string.Join(", ", healthList.ConvertAll(c => $"{c.Name}={c.Status}"))}");
            ChannelHealthUpdated?.Invoke(this, healthList.ToArray());
        }
    }

    private void ParseSessions(JsonElement sessions)
    {
        try
        {
            _sessions.Clear();
            
            // Handle both Array format and Object (dictionary) format
            if (sessions.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in sessions.EnumerateArray())
                {
                    ParseSessionItem(item);
                }
            }
            else if (sessions.ValueKind == JsonValueKind.Object)
            {
                // Object format: keys are session IDs, values could be session info objects or simple strings
                foreach (var prop in sessions.EnumerateObject())
                {
                    var sessionKey = prop.Name;
                    
                    // Skip metadata fields that aren't actual sessions
                    if (sessionKey is "recent" or "count" or "path" or "defaults" or "ts")
                        continue;
                    
                    // Skip non-session keys (must look like a session key pattern)
                    if (!sessionKey.Contains(':') && !sessionKey.Contains("agent") && !sessionKey.Contains("session"))
                        continue;
                    
                    var session = new SessionInfo { Key = sessionKey };
                    var item = prop.Value;
                    
                    // Detect main session from key pattern - "agent:main:main" ends with ":main"
                    var endsWithMain = sessionKey.EndsWith(":main");
                    session.IsMain = sessionKey == "main" || endsWithMain || sessionKey.Contains(":main:main");
                    _logger.Info($"Session key={sessionKey}, endsWithMain={endsWithMain}, IsMain={session.IsMain}");
                    
                    // Value might be an object with session details or just a string status
                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        // Only override IsMain if the JSON explicitly says true
                        if (item.TryGetProperty("isMain", out var isMain) && isMain.GetBoolean())
                            session.IsMain = true;
                        if (item.TryGetProperty("status", out var status))
                            session.Status = status.GetString() ?? "active";
                        if (item.TryGetProperty("model", out var model))
                            session.Model = model.GetString();
                        if (item.TryGetProperty("channel", out var channel))
                            session.Channel = channel.GetString();
                        if (item.TryGetProperty("startedAt", out var started))
                        {
                            if (DateTime.TryParse(started.GetString(), out var dt))
                                session.StartedAt = dt;
                        }
                    }
                    else if (item.ValueKind == JsonValueKind.String)
                    {
                        // Simple string value - skip if it looks like a path (metadata)
                        var strVal = item.GetString() ?? "";
                        if (strVal.StartsWith("/") || strVal.Contains("/."))
                            continue;
                        session.Status = strVal;
                    }
                    else if (item.ValueKind == JsonValueKind.Number)
                    {
                        // Skip numeric values (like count)
                        continue;
                    }
                    
                    _sessions[session.Key] = session;
                }
            }

            SessionsUpdated?.Invoke(this, GetSessionList());
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to parse sessions: {ex.Message}");
        }
    }
    
    private void ParseSessionItem(JsonElement item)
    {
        var session = new SessionInfo();
        if (item.TryGetProperty("key", out var key))
            session.Key = key.GetString() ?? "unknown";
        
        // Detect main from key pattern first
        session.IsMain = session.Key == "main" || 
                         session.Key.EndsWith(":main") ||
                         session.Key.Contains(":main:main");
        
        // Only override if JSON explicitly says true
        if (item.TryGetProperty("isMain", out var isMain) && isMain.GetBoolean())
            session.IsMain = true;
            
        if (item.TryGetProperty("status", out var status))
            session.Status = status.GetString() ?? "unknown";
        if (item.TryGetProperty("model", out var model))
            session.Model = model.GetString();
        if (item.TryGetProperty("channel", out var channel))
            session.Channel = channel.GetString();
        if (item.TryGetProperty("startedAt", out var started))
        {
            if (DateTime.TryParse(started.GetString(), out var dt))
                session.StartedAt = dt;
        }

        _sessions[session.Key] = session;
    }

    private void ParseUsage(JsonElement usage)
    {
        try
        {
            _usage = new GatewayUsageInfo();
            if (usage.TryGetProperty("inputTokens", out var inp))
                _usage.InputTokens = inp.GetInt64();
            if (usage.TryGetProperty("outputTokens", out var outp))
                _usage.OutputTokens = outp.GetInt64();
            if (usage.TryGetProperty("totalTokens", out var tot))
                _usage.TotalTokens = tot.GetInt64();
            if (usage.TryGetProperty("cost", out var cost))
                _usage.CostUsd = cost.GetDouble();
            if (usage.TryGetProperty("requestCount", out var req))
                _usage.RequestCount = req.GetInt32();
            if (usage.TryGetProperty("model", out var model))
                _usage.Model = model.GetString();

            UsageUpdated?.Invoke(this, _usage);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to parse usage: {ex.Message}");
        }
    }

    // --- Notification classification ---

    private static readonly NotificationCategorizer _categorizer = new();

    private void EmitNotification(string text)
    {
        var notification = new OpenClawNotification
        {
            Message = text.Length > 200 ? text[..200] + "…" : text
        };
        var (title, type) = _categorizer.Classify(notification);
        notification.Title = title;
        notification.Type = type;
        NotificationReceived?.Invoke(this, notification);
    }

    private static (string title, string type) ClassifyNotification(string text)
    {
        return NotificationCategorizer.ClassifyByKeywords(text);
    }

    // --- Utility ---

    private static ActivityKind ClassifyTool(string toolName)
    {
        return toolName.ToLowerInvariant() switch
        {
            "exec" => ActivityKind.Exec,
            "read" => ActivityKind.Read,
            "write" => ActivityKind.Write,
            "edit" => ActivityKind.Edit,
            "web_search" => ActivityKind.Search,
            "web_fetch" => ActivityKind.Search,
            "browser" => ActivityKind.Browser,
            "message" => ActivityKind.Message,
            "tts" => ActivityKind.Tool,
            "image" => ActivityKind.Tool,
            _ => ActivityKind.Tool
        };
    }

    private static string ShortenPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        var parts = path.Replace('\\', '/').Split('/');
        return parts.Length > 2
            ? $"…/{parts[^2]}/{parts[^1]}"
            : parts[^1];
    }

    private static string TruncateLabel(string text, int maxLen = 60)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLen) return text;
        return text[..(maxLen - 1)] + "…";
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _cts.Cancel();
            _webSocket?.Dispose();
            _cts.Dispose();
        }
    }
}
