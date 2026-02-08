namespace OpenClaw.Shared;

public enum ConnectionStatus
{
    Disconnected,
    Connecting,
    Connected,
    Error
}

public enum PairingStatus
{
    Unknown,
    Pending,    // Connected but awaiting approval
    Paired,     // Approved with device token
    Rejected    // Pairing was rejected
}

public class PairingStatusEventArgs : EventArgs
{
    public PairingStatus Status { get; }
    public string DeviceId { get; }
    public string? Message { get; }
    
    public PairingStatusEventArgs(PairingStatus status, string deviceId, string? message = null)
    {
        Status = status;
        DeviceId = deviceId;
        Message = message;
    }
}

public enum ActivityKind
{
    Idle,
    Job,
    Exec,
    Read,
    Write,
    Edit,
    Search,
    Browser,
    Message,
    Tool
}

public class AgentActivity
{
    public string SessionKey { get; set; } = "";
    public bool IsMain { get; set; }
    public ActivityKind Kind { get; set; } = ActivityKind.Idle;
    public string State { get; set; } = "";
    public string ToolName { get; set; } = "";
    public string Label { get; set; } = "";

    public string Glyph => Kind switch
    {
        ActivityKind.Exec => "💻",
        ActivityKind.Read => "📄",
        ActivityKind.Write => "✍️",
        ActivityKind.Edit => "📝",
        ActivityKind.Search => "🔍",
        ActivityKind.Browser => "🌐",
        ActivityKind.Message => "💬",
        ActivityKind.Tool => "🛠️",
        ActivityKind.Job => "⚡",
        _ => ""
    };

    public string DisplayText => Kind == ActivityKind.Idle
        ? ""
        : $"{(IsMain ? "Main" : "Sub")} · {Glyph} {Label}";
}

public class OpenClawNotification
{
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string Type { get; set; } = "";
    public bool IsChat { get; set; } = false; // True if from chat response

    // Structured metadata (populated by gateway when available)
    public string? Channel { get; set; }   // e.g. telegram, email, chat
    public string? Agent { get; set; }     // agent name/identifier
    public string? Intent { get; set; }    // normalized intent (reminder, build, alert)
    public string[]? Tags { get; set; }    // free-form routing tags
}

/// <summary>
/// A user-defined notification categorization rule.
/// </summary>
public class UserNotificationRule
{
    public string Pattern { get; set; } = "";
    public bool IsRegex { get; set; }
    public string Category { get; set; } = "info";
    public bool Enabled { get; set; } = true;
}

public class ChannelHealth
{
    public string Name { get; set; } = "";
    public string Status { get; set; } = "unknown";
    public bool IsLinked { get; set; }
    public string? Error { get; set; }
    public string? AuthAge { get; set; }
    public string? Type { get; set; }

    public string DisplayText
    {
        get
        {
            var label = Status.ToLowerInvariant() switch
            {
                "ok" or "connected" or "running" => "[ON]",
                "linked" => "[LINKED]",
                "ready" => "[READY]",
                "connecting" or "reconnecting" => "[...]",
                "error" or "disconnected" => "[ERR]",
                "stale" => "[STALE]",
                "configured" or "stopped" => "[OFF]",
                "not configured" => "[N/A]",
                _ => "[OFF]"
            };
            var detail = IsLinked && AuthAge != null ? $"linked · {AuthAge}" : Status;
            if (Error != null) detail += $" ({Error})";
            return $"{label} {Capitalize(Name)}: {detail}";
        }
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];
}

public class SessionInfo
{
    public string Key { get; set; } = "";
    public bool IsMain { get; set; }
    public string Status { get; set; } = "unknown";
    public string? Model { get; set; }
    public string? Channel { get; set; }
    public string? CurrentActivity { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;

    public string DisplayText
    {
        get
        {
            var prefix = IsMain ? "Main" : "Sub";
            var parts = new List<string> { prefix };

            if (!string.IsNullOrEmpty(Channel))
                parts.Add(Channel);

            if (!string.IsNullOrEmpty(CurrentActivity))
                parts.Add(CurrentActivity);
            else if (!string.IsNullOrEmpty(Status) && Status != "unknown" && Status != "active")
                parts.Add(Status);

            return string.Join(" · ", parts);
        }
    }
    
    /// <summary>Gets a shortened, user-friendly version of the session key.</summary>
    public string ShortKey
    {
        get
        {
            if (string.IsNullOrEmpty(Key)) return "unknown";
            
            // Extract meaningful part from session keys like "agent:main:subagent:uuid"
            var parts = Key.Split(':');
            if (parts.Length >= 3)
            {
                // Return something like "subagent" or "cron" 
                return parts[^2]; // Second to last part
            }
            
            // For file paths, just return filename
            if (Key.Contains('/') || Key.Contains('\\'))
            {
                return Path.GetFileName(Key);
            }
            
            return Key.Length > 20 ? Key[..17] + "..." : Key;
        }
    }
}

public class GatewayUsageInfo
{
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long TotalTokens { get; set; }
    public double CostUsd { get; set; }
    public int RequestCount { get; set; }
    public string? Model { get; set; }

    public string DisplayText
    {
        get
        {
            var parts = new List<string>();
            if (TotalTokens > 0)
                parts.Add($"Tokens: {FormatCount(TotalTokens)}");
            if (CostUsd > 0)
                parts.Add($"${CostUsd:F2}");
            if (RequestCount > 0)
                parts.Add($"{RequestCount} requests");
            if (!string.IsNullOrEmpty(Model))
                parts.Add(Model);
            return parts.Count > 0
                ? string.Join(" · ", parts)
                : "No usage data";
        }
    }

    private static string FormatCount(long n)
    {
        if (n >= 1_000_000) return $"{n / 1_000_000.0:F1}M";
        if (n >= 1_000) return $"{n / 1_000.0:F1}K";
        return n.ToString();
    }
}

