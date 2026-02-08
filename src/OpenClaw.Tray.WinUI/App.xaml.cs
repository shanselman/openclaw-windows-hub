using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenClaw.Shared;
using OpenClaw.Shared.Capabilities;
using OpenClawTray.Dialogs;
using OpenClawTray.Helpers;
using OpenClawTray.Services;
using OpenClawTray.Windows;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Updatum;
using WinUIEx;

namespace OpenClawTray;

public partial class App : Application
{
    private const string PipeName = "OpenClawTray-DeepLink";
    
    internal static readonly UpdatumManager AppUpdater = new("shanselman", "openclaw-windows-hub")
    {
        FetchOnlyLatestRelease = true,
        InstallUpdateSingleFileExecutableName = "OpenClaw.Tray.WinUI",
    };

    private TrayIcon? _trayIcon;
    private OpenClawGatewayClient? _gatewayClient;
    private SettingsManager? _settings;
    private GlobalHotkeyService? _globalHotkey;
    private System.Timers.Timer? _healthCheckTimer;
    private System.Timers.Timer? _sessionPollTimer;
    private Mutex? _mutex;
    private Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue;
    private CancellationTokenSource? _deepLinkCts;
    
    private ConnectionStatus _currentStatus = ConnectionStatus.Disconnected;
    private AgentActivity? _currentActivity;
    private ChannelHealth[] _lastChannels = Array.Empty<ChannelHealth>();
    private SessionInfo[] _lastSessions = Array.Empty<SessionInfo>();
    private GatewayUsageInfo? _lastUsage;
    private DateTime _lastCheckTime = DateTime.Now;

    // Session-aware activity tracking
    private readonly Dictionary<string, AgentActivity> _sessionActivities = new();
    private string? _displayedSessionKey;
    private DateTime _lastSessionSwitch = DateTime.MinValue;
    private static readonly TimeSpan SessionSwitchDebounce = TimeSpan.FromSeconds(3);

    // Windows (created on demand)
    private SettingsWindow? _settingsWindow;
    private WebChatWindow? _webChatWindow;
    private StatusDetailWindow? _statusDetailWindow;
    private NotificationHistoryWindow? _notificationHistoryWindow;
    private TrayMenuWindow? _trayMenuWindow;
    
    // Node service (optional, enabled in settings)
    private NodeService? _nodeService;
    
    // Keep-alive window to anchor WinUI runtime (prevents GC/threading issues)
    private Window? _keepAliveWindow;

    private string[]? _startupArgs;
    private string? _pendingProtocolUri;
    private static readonly string DataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenClawTray");
    private static readonly string CrashLogPath = Path.Combine(DataPath, "crash.log");
    private static readonly string RunMarkerPath = Path.Combine(DataPath, "run.marker");

    public App()
    {
        InitializeComponent();
        
        CheckPreviousRun();
        MarkRunStarted();
        
        // Hook up crash handlers
        this.UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        LogCrash("UnhandledException", e.Exception);
        e.Handled = true; // Try to prevent crash
    }

    private void OnDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        LogCrash("DomainUnhandledException", e.ExceptionObject as Exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogCrash("UnobservedTaskException", e.Exception);
        e.SetObserved(); // Prevent crash
    }
    
    private void OnProcessExit(object? sender, EventArgs e)
    {
        MarkRunEnded();
        try
        {
            Logger.Info($"Process exiting (ExitCode={Environment.ExitCode})");
        }
        catch { }
    }

    private static void LogCrash(string source, Exception? ex)
    {
        try
        {
            var dir = Path.GetDirectoryName(CrashLogPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var message = $"\n[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {source}\n{ex}\n";
            File.AppendAllText(CrashLogPath, message);
        }
        catch { /* Can't log the crash logger crash */ }
        
        try
        {
            if (ex != null)
            {
                Logger.Error($"CRASH {source}: {ex}");
            }
            else
            {
                Logger.Error($"CRASH {source}");
            }
        }
        catch { /* Ignore logging failures */ }
    }
    
    private static void CheckPreviousRun()
    {
        try
        {
            if (File.Exists(RunMarkerPath))
            {
                var startedAt = File.ReadAllText(RunMarkerPath);
                Logger.Error($"Previous session did not exit cleanly (started {startedAt})");
                File.Delete(RunMarkerPath);
            }
        }
        catch { }
    }
    
    private static void MarkRunStarted()
    {
        try
        {
            var dir = Path.GetDirectoryName(RunMarkerPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(RunMarkerPath, DateTime.Now.ToString("O"));
        }
        catch { }
    }
    
    private static void MarkRunEnded()
    {
        try
        {
            if (File.Exists(RunMarkerPath))
                File.Delete(RunMarkerPath);
        }
        catch { }
    }

    /// <summary>
    /// Check if the app was launched via protocol activation (MSIX deep link).
    /// In WinUI 3, protocol activation is retrieved via AppInstance, not OnActivated.
    /// </summary>
    private static string? GetProtocolActivationUri()
    {
        try
        {
            var activatedArgs = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs();
            if (activatedArgs.Kind == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.Protocol
                && activatedArgs.Data is global::Windows.ApplicationModel.Activation.IProtocolActivatedEventArgs protocolArgs)
            {
                return protocolArgs.Uri?.ToString();
            }
        }
        catch { /* Not activated via protocol, or not packaged */ }
        return null;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _startupArgs = Environment.GetCommandLineArgs();
        _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        // Check for protocol activation (MSIX packaged apps receive deep links this way)
        string? protocolUri = GetProtocolActivationUri();

        // Single instance check - keep mutex alive for app lifetime
        _mutex = new Mutex(true, "OpenClawTray", out bool createdNew);
        if (!createdNew)
        {
            // Forward deep link args to running instance (command-line or protocol activation)
            var deepLink = protocolUri
                ?? (_startupArgs.Length > 1 && _startupArgs[1].StartsWith("openclaw://", StringComparison.OrdinalIgnoreCase)
                    ? _startupArgs[1] : null);
            if (deepLink != null)
            {
                SendDeepLinkToRunningInstance(deepLink);
            }
            Exit();
            return;
        }

        // Store protocol URI for processing after setup
        _pendingProtocolUri = protocolUri;

        // Register URI scheme on first run
        DeepLinkHandler.RegisterUriScheme();

        // Check for updates before launching
        var shouldLaunch = await CheckForUpdatesAsync();
        if (!shouldLaunch)
        {
            Exit();
            return;
        }

        // Register toast activation handler
        ToastNotificationManagerCompat.OnActivated += OnToastActivated;

        // Initialize settings
        _settings = new SettingsManager();

        // First-run check
        if (string.IsNullOrWhiteSpace(_settings.Token))
        {
            await ShowFirstRunWelcomeAsync();
        }

        // Initialize tray icon (window-less pattern from WinUIEx)
        InitializeTrayIcon();

        // Initialize connections - only use operator if node mode is disabled
        // (dual connections cause gateway conflicts)
        if (_settings?.EnableNodeMode == true)
        {
            // Node mode: only use node connection (provides health events too)
            InitializeNodeService();
        }
        else
        {
            // Operator mode: use operator connection
            InitializeGatewayClient();
        }

        // Start health check timer
        StartHealthCheckTimer();

        // Start deep link server
        StartDeepLinkServer();

        // Register global hotkey if enabled
        if (_settings.GlobalHotkeyEnabled)
        {
            _globalHotkey = new GlobalHotkeyService();
            _globalHotkey.HotkeyPressed += OnGlobalHotkeyPressed;
            _globalHotkey.Register();
        }

        // Process startup deep link (command-line or MSIX protocol activation)
        var startupDeepLink = _pendingProtocolUri
            ?? (_startupArgs.Length > 1 && _startupArgs[1].StartsWith("openclaw://", StringComparison.OrdinalIgnoreCase)
                ? _startupArgs[1] : null);
        if (startupDeepLink != null)
        {
            HandleDeepLink(startupDeepLink);
        }

        Logger.Info("Application started (WinUI 3)");
    }

    private void InitializeKeepAliveWindow()
    {
        // Create a hidden window to keep the WinUI runtime properly initialized
        // This prevents GC/threading issues when creating windows after idle
        _keepAliveWindow = new Window();
        _keepAliveWindow.Content = new Microsoft.UI.Xaml.Controls.Grid();
        _keepAliveWindow.AppWindow.IsShownInSwitchers = false;
        
        // Move off-screen and set minimal size
        _keepAliveWindow.AppWindow.MoveAndResize(new global::Windows.Graphics.RectInt32(-32000, -32000, 1, 1));
    }

    private void InitializeTrayIcon()
    {
        // Initialize keep-alive window first to anchor WinUI runtime
        InitializeKeepAliveWindow();
        
        // Pre-create tray menu window at startup to avoid creation crashes later
        InitializeTrayMenuWindow();
        
        var iconPath = IconHelper.GetStatusIconPath(ConnectionStatus.Disconnected);
        _trayIcon = new TrayIcon(1, iconPath, "OpenClaw Tray — Disconnected");
        _trayIcon.IsVisible = true;
        _trayIcon.Selected += OnTrayIconSelected;
        _trayIcon.ContextMenu += OnTrayContextMenu;
    }

    private void InitializeTrayMenuWindow()
    {
        // Pre-create menu window once - reuse to avoid crash on window creation after idle
        _trayMenuWindow = new TrayMenuWindow();
        _trayMenuWindow.MenuItemClicked += OnTrayMenuItemClicked;
        // Don't close - just hide
    }

    private void OnTrayIconSelected(TrayIcon sender, TrayIconEventArgs e)
    {
        // Left-click: show custom popup menu
        ShowTrayMenuPopup();
    }

    private void OnTrayContextMenu(TrayIcon sender, TrayIconEventArgs e)
    {
        // Right-click: show custom popup menu
        ShowTrayMenuPopup();
    }

    private MenuFlyout BuildTrayMenuFlyout()
    {
        // Pre-fetch data (fire and forget - flyout will show with cached data)
        if (_gatewayClient != null && _currentStatus == ConnectionStatus.Connected)
        {
            try
            {
                _ = _gatewayClient.CheckHealthAsync();
                _ = _gatewayClient.RequestSessionsAsync();
                _ = _gatewayClient.RequestUsageAsync();
            }
            catch { /* ignore */ }
        }

        var flyout = new MenuFlyout();
        
        // Brand header
        var header = new MenuFlyoutItem { Text = "🦞 Molty", IsEnabled = false };
        header.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
        flyout.Items.Add(header);
        flyout.Items.Add(new MenuFlyoutSeparator());

        // Status
        var statusIcon = _currentStatus switch
        {
            ConnectionStatus.Connected => "✅",
            ConnectionStatus.Connecting => "🔄",
            ConnectionStatus.Error => "❌",
            _ => "⚪"
        };
        var statusItem = new MenuFlyoutItem { Text = $"{statusIcon} Status: {_currentStatus}" };
        statusItem.Click += (s, e) => ShowStatusDetail();
        flyout.Items.Add(statusItem);

        // Activity (if any)
        if (_currentActivity != null && _currentActivity.Kind != OpenClaw.Shared.ActivityKind.Idle)
        {
            flyout.Items.Add(new MenuFlyoutItem 
            { 
                Text = $"{_currentActivity.Glyph} {_currentActivity.DisplayText}", 
                IsEnabled = false 
            });
        }

        // Usage
        if (_lastUsage != null)
        {
            flyout.Items.Add(new MenuFlyoutItem 
            { 
                Text = $"📊 {_lastUsage.DisplayText}", 
                IsEnabled = false 
            });
        }

        flyout.Items.Add(new MenuFlyoutSeparator());

        // Sessions
        if (_lastSessions.Length > 0)
        {
            var sessionsMenu = new MenuFlyoutSubItem { Text = $"📋 Sessions ({_lastSessions.Length})" };
            foreach (var session in _lastSessions.Take(5))
            {
                var sessionItem = new MenuFlyoutItem { Text = session.DisplayText };
                var sessionKey = session.Key;
                sessionItem.Click += (s, e) => OpenDashboard($"sessions/{sessionKey}");
                sessionsMenu.Items.Add(sessionItem);
            }
            flyout.Items.Add(sessionsMenu);
        }

        // Quick actions
        var dashboardItem = new MenuFlyoutItem { Text = "🌐 Open Dashboard" };
        dashboardItem.Click += (s, e) => OpenDashboard();
        flyout.Items.Add(dashboardItem);

        var chatItem = new MenuFlyoutItem { Text = "💬 Web Chat" };
        chatItem.Click += (s, e) => ShowWebChat();
        flyout.Items.Add(chatItem);

        var quickSendItem = new MenuFlyoutItem { Text = "✉️ Quick Send" };
        quickSendItem.Click += (s, e) => ShowQuickSend();
        flyout.Items.Add(quickSendItem);

        var historyItem = new MenuFlyoutItem { Text = "📜 Notification History" };
        historyItem.Click += (s, e) => ShowNotificationHistory();
        flyout.Items.Add(historyItem);

        flyout.Items.Add(new MenuFlyoutSeparator());

        // Settings & Exit
        var settingsItem = new MenuFlyoutItem { Text = "⚙️ Settings" };
        settingsItem.Click += (s, e) => ShowSettings();
        flyout.Items.Add(settingsItem);

        var logItem = new MenuFlyoutItem { Text = "📄 View Log" };
        logItem.Click += (s, e) => OpenLogFile();
        flyout.Items.Add(logItem);

        flyout.Items.Add(new MenuFlyoutSeparator());

        var exitItem = new MenuFlyoutItem { Text = "❌ Exit" };
        exitItem.Click += (s, e) => ExitApplication();
        flyout.Items.Add(exitItem);

        return flyout;
    }

    private async void ShowTrayMenuPopup()
    {
        try
        {
            // Verify dispatcher is still valid
            if (_dispatcherQueue == null)
            {
                Logger.Error("DispatcherQueue is null - cannot show menu");
                return;
            }

            // Pre-fetch latest data before showing menu
            if (_gatewayClient != null && _currentStatus == ConnectionStatus.Connected)
            {
                try
                {
                    // Request fresh data
                    _ = _gatewayClient.CheckHealthAsync();
                    _ = _gatewayClient.RequestSessionsAsync();
                    _ = _gatewayClient.RequestUsageAsync();
                    
                    // Only wait if we have NO cached session data
                    // Otherwise show instantly with cached data (feels snappier)
                    if (_lastSessions.Length == 0)
                    {
                        await Task.Delay(200); // Wait for first-time data
                    }
                    else
                    {
                        await Task.Delay(50); // Brief yield to let fresh data arrive if ready
                    }
                    
                    Logger.Info($"Menu data: {_lastSessions.Length} sessions, {_lastChannels.Length} channels");
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Data fetch error: {ex.Message}");
                }
            }

            // Reuse pre-created window - never create new ones after startup
            if (_trayMenuWindow == null)
            {
                // This shouldn't happen, but recreate if needed
                Logger.Warn("TrayMenuWindow was null, recreating");
                InitializeTrayMenuWindow();
            }

            // Rebuild menu content
            _trayMenuWindow!.ClearItems();
            BuildTrayMenuPopup(_trayMenuWindow);
            _trayMenuWindow.SizeToContent();
            _trayMenuWindow.ShowAtCursor();
        }
        catch (Exception ex)
        {
            LogCrash("ShowTrayMenuPopup", ex);
            Logger.Error($"Failed to show tray menu: {ex.Message}");
        }
    }

    private void OnTrayMenuItemClicked(object? sender, string action)
    {
        switch (action)
        {
            case "status": ShowStatusDetail(); break;
            case "dashboard": OpenDashboard(); break;
            case "webchat": ShowWebChat(); break;
            case "quicksend": ShowQuickSend(); break;
            case "history": ShowNotificationHistory(); break;
            case "healthcheck": _ = RunHealthCheckAsync(); break;
            case "settings": ShowSettings(); break;
            case "autostart": ToggleAutoStart(); break;
            case "log": OpenLogFile(); break;
            case "copydeviceid": CopyDeviceIdToClipboard(); break;
            case "exit": ExitApplication(); break;
            default:
                if (action.StartsWith("session:"))
                    OpenDashboard($"sessions/{action[8..]}");
                else if (action.StartsWith("channel:"))
                    ToggleChannel(action[8..]);
                break;
        }
    }
    
    private void CopyDeviceIdToClipboard()
    {
        if (_nodeService?.FullDeviceId == null) return;
        
        try
        {
            var dataPackage = new global::Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(_nodeService.FullDeviceId);
            global::Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
            
            // Show toast confirming copy
            new ToastContentBuilder()
                .AddText("📋 Device ID Copied")
                .AddText($"Run: openclaw devices approve {_nodeService.ShortDeviceId}...")
                .Show();
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to copy device ID: {ex.Message}");
        }
    }

    private void BuildTrayMenuPopup(TrayMenuWindow menu)
    {
        // Brand header
        menu.AddBrandHeader("🦞", "Molty");
        menu.AddSeparator();

        // Status
        var statusIcon = _currentStatus switch
        {
            ConnectionStatus.Connected => "✅",
            ConnectionStatus.Connecting => "🔄",
            ConnectionStatus.Error => "❌",
            _ => "⚪"
        };
        menu.AddMenuItem($"Status: {_currentStatus}", statusIcon, "status");

        // Activity (if any)
        if (_currentActivity != null && _currentActivity.Kind != OpenClaw.Shared.ActivityKind.Idle)
        {
            menu.AddMenuItem(_currentActivity.DisplayText, _currentActivity.Glyph, "", isEnabled: false);
        }

        // Usage
        if (_lastUsage != null)
        {
            menu.AddMenuItem(_lastUsage.DisplayText, "📊", "", isEnabled: false);
        }
        
        // Node Mode status (if enabled)
        if (_settings?.EnableNodeMode == true && _nodeService != null)
        {
            menu.AddSeparator();
            menu.AddHeader("🔌 Node Mode");
            
            if (_nodeService.IsPendingApproval)
            {
                menu.AddMenuItem("⏳ Waiting for approval...", "", "", isEnabled: false, indent: true);
                menu.AddMenuItem($"ID: {_nodeService.ShortDeviceId}...", "", "copydeviceid", indent: true);
            }
            else if (_nodeService.IsPaired && _nodeService.IsConnected)
            {
                menu.AddMenuItem("✅ Paired & Connected", "", "", isEnabled: false, indent: true);
            }
            else if (_nodeService.IsConnected)
            {
                menu.AddMenuItem("🔄 Connecting...", "", "", isEnabled: false, indent: true);
            }
            else
            {
                menu.AddMenuItem("⚪ Disconnected", "", "", isEnabled: false, indent: true);
            }
        }

        // Sessions (if any) - show meaningful info like the WinForms version
        if (_lastSessions.Length > 0)
        {
            menu.AddSeparator();
            menu.AddMenuItem($"Sessions ({_lastSessions.Length})", "💬", "dashboard:sessions");

            foreach (var session in _lastSessions.Take(5))
            {
                // Extract session type from key like "agent:main:cron:uuid" or "agent:main:subagent:uuid"
                var parts = session.Key.Split(':');
                var sessionType = parts.Length >= 3 ? parts[2] : "session";
                var displayName = sessionType switch
                {
                    "main" => "Main Agent",
                    "cron" => "Scheduled Task",
                    "subagent" => "Sub-Agent",
                    _ => sessionType.Length > 0 ? char.ToUpper(sessionType[0]) + sessionType[1..] : "Session"
                };
                
                // Add model if available
                if (!string.IsNullOrEmpty(session.Model))
                    displayName += $" ({session.Model})";
                else if (!string.IsNullOrEmpty(session.Channel))
                    displayName += $" · {session.Channel}";
                    
                var icon = session.IsMain ? "⭐" : "•";
                menu.AddMenuItem(displayName, icon, $"session:{session.Key}", isEnabled: false, indent: true);
            }
            if (_lastSessions.Length > 5)
                menu.AddMenuItem($"+{_lastSessions.Length - 5} more...", "", "", isEnabled: false, indent: true);
        }

        // Channels (if any)
        if (_lastChannels.Length > 0)
        {
            menu.AddSeparator();
            menu.AddHeader("📡 Channels");

            foreach (var channel in _lastChannels)
            {
                var rawStatus = channel.Status?.ToLowerInvariant() ?? "";
                
                // Match status logic from WinForms version
                var channelIcon = rawStatus switch
                {
                    "ok" or "connected" or "running" or "active" or "ready" => "🟢",
                    "stopped" or "idle" or "paused" or "configured" or "pending" => "🟡",
                    "error" or "disconnected" or "failed" => "🔴",
                    _ => "⚪"
                };
                
                var channelName = char.ToUpper(channel.Name[0]) + channel.Name[1..];
                menu.AddMenuItem(channelName, channelIcon, $"channel:{channel.Name}", indent: true);
            }
        }

        menu.AddSeparator();

        // Actions
        menu.AddMenuItem("Open Dashboard", "🌐", "dashboard");
        menu.AddMenuItem("Open Web Chat", "💬", "webchat");
        menu.AddMenuItem("Quick Send...", "📤", "quicksend");
        menu.AddMenuItem("Notification History...", "📋", "history");
        menu.AddMenuItem("Run Health Check", "🔄", "healthcheck");

        menu.AddSeparator();

        // Settings
        menu.AddMenuItem("Settings...", "⚙️", "settings");
        var autoStartText = (_settings?.AutoStart ?? false) ? "Auto-start ✓" : "Auto-start";
        menu.AddMenuItem(autoStartText, "🚀", "autostart");

        menu.AddSeparator();

        menu.AddMenuItem("Open Log File", "📄", "log");
        menu.AddMenuItem("Exit", "❌", "exit");
    }

    // Keep the old MenuFlyout method for reference but it won't be used
    private void BuildTrayMenu(MenuFlyout flyout)
    {
        // Brand header
        var brandItem = new MenuFlyoutItem
        {
            Text = "🦞 OpenClaw Tray",
            IsEnabled = false
        };
        flyout.Items.Add(brandItem);
        flyout.Items.Add(new MenuFlyoutSeparator());

        // Status
        var statusIcon = _currentStatus switch
        {
            ConnectionStatus.Connected => "✅",
            ConnectionStatus.Connecting => "🔄",
            ConnectionStatus.Error => "❌",
            _ => "⚪"
        };
        var statusItem = new MenuFlyoutItem
        {
            Text = $"{statusIcon} Status: {_currentStatus}"
        };
        statusItem.Click += (s, e) => ShowStatusDetail();
        flyout.Items.Add(statusItem);

        // Activity (if any)
        if (_currentActivity != null && _currentActivity.Kind != OpenClaw.Shared.ActivityKind.Idle)
        {
            var activityItem = new MenuFlyoutItem
            {
                Text = $"{_currentActivity.Glyph} {_currentActivity.DisplayText}",
                IsEnabled = false
            };
            flyout.Items.Add(activityItem);
        }

        // Usage
        if (_lastUsage != null)
        {
            var usageItem = new MenuFlyoutItem
            {
                Text = $"📊 {_lastUsage.DisplayText}",
                IsEnabled = false
            };
            flyout.Items.Add(usageItem);
        }

        // Sessions
        if (_lastSessions.Length > 0)
        {
            flyout.Items.Add(new MenuFlyoutSeparator());
            var sessionsHeader = new MenuFlyoutItem
            {
                Text = $"💬 Sessions ({_lastSessions.Length})"
            };
            sessionsHeader.Click += (s, e) => OpenDashboard("sessions");
            flyout.Items.Add(sessionsHeader);

            foreach (var session in _lastSessions.Take(5))
            {
                var sessionItem = new MenuFlyoutItem
                {
                    Text = $"   • {session.DisplayText}"
                };
                sessionItem.Click += (s, e) => OpenDashboard($"sessions/{session.Key}");
                flyout.Items.Add(sessionItem);
            }
        }

        // Channels
        if (_lastChannels.Length > 0)
        {
            flyout.Items.Add(new MenuFlyoutSeparator());
            var channelsHeader = new MenuFlyoutItem
            {
                Text = "📡 Channels",
                IsEnabled = false
            };
            flyout.Items.Add(channelsHeader);

            foreach (var channel in _lastChannels)
            {
                var channelIcon = channel.Status?.ToLowerInvariant() switch
                {
                    "ok" or "connected" or "running" => "🟢",
                    "connecting" or "reconnecting" => "🟡",
                    _ => "🔴"
                };
                var channelItem = new MenuFlyoutItem
                {
                    Text = $"   {channelIcon} {channel.Name}"
                };
                channelItem.Click += (s, e) => ToggleChannel(channel.Name);
                flyout.Items.Add(channelItem);
            }
        }

        flyout.Items.Add(new MenuFlyoutSeparator());

        // Actions
        var dashboardItem = new MenuFlyoutItem { Text = "🌐 Open Dashboard" };
        dashboardItem.Click += (s, e) => OpenDashboard();
        flyout.Items.Add(dashboardItem);

        var webChatItem = new MenuFlyoutItem { Text = "💬 Open Web Chat" };
        webChatItem.Click += (s, e) => ShowWebChat();
        flyout.Items.Add(webChatItem);

        var quickSendItem = new MenuFlyoutItem { Text = "📤 Quick Send..." };
        quickSendItem.Click += (s, e) => ShowQuickSend();
        flyout.Items.Add(quickSendItem);

        var historyItem = new MenuFlyoutItem { Text = "📋 Notification History..." };
        historyItem.Click += (s, e) => ShowNotificationHistory();
        flyout.Items.Add(historyItem);

        var healthCheckItem = new MenuFlyoutItem { Text = "🔄 Run Health Check" };
        healthCheckItem.Click += async (s, e) => await RunHealthCheckAsync();
        flyout.Items.Add(healthCheckItem);

        flyout.Items.Add(new MenuFlyoutSeparator());

        // Settings
        var settingsItem = new MenuFlyoutItem { Text = "⚙️ Settings..." };
        settingsItem.Click += (s, e) => ShowSettings();
        flyout.Items.Add(settingsItem);

        var autoStartItem = new ToggleMenuFlyoutItem
        {
            Text = "🚀 Auto-start",
            IsChecked = _settings?.AutoStart ?? false
        };
        autoStartItem.Click += (s, e) => ToggleAutoStart();
        flyout.Items.Add(autoStartItem);

        flyout.Items.Add(new MenuFlyoutSeparator());

        var logItem = new MenuFlyoutItem { Text = "📄 Open Log File" };
        logItem.Click += (s, e) => OpenLogFile();
        flyout.Items.Add(logItem);

        var exitItem = new MenuFlyoutItem { Text = "❌ Exit" };
        exitItem.Click += (s, e) => ExitApplication();
        flyout.Items.Add(exitItem);
    }

    #region Gateway Client

    private void InitializeGatewayClient()
    {
        if (_settings == null) return;

        // Unsubscribe from old client if exists
        UnsubscribeGatewayEvents();

        _gatewayClient = new OpenClawGatewayClient(_settings.GatewayUrl, _settings.Token, new AppLogger());
        _gatewayClient.StatusChanged += OnConnectionStatusChanged;
        _gatewayClient.ActivityChanged += OnActivityChanged;
        _gatewayClient.NotificationReceived += OnNotificationReceived;
        _gatewayClient.ChannelHealthUpdated += OnChannelHealthUpdated;
        _gatewayClient.SessionsUpdated += OnSessionsUpdated;
        _gatewayClient.UsageUpdated += OnUsageUpdated;
        _ = _gatewayClient.ConnectAsync();
    }

    private void UnsubscribeGatewayEvents()
    {
        if (_gatewayClient != null)
        {
            _gatewayClient.StatusChanged -= OnConnectionStatusChanged;
            _gatewayClient.ActivityChanged -= OnActivityChanged;
            _gatewayClient.NotificationReceived -= OnNotificationReceived;
            _gatewayClient.ChannelHealthUpdated -= OnChannelHealthUpdated;
            _gatewayClient.SessionsUpdated -= OnSessionsUpdated;
            _gatewayClient.UsageUpdated -= OnUsageUpdated;
        }
    }
    
    private void InitializeNodeService()
    {
        if (_settings == null || !_settings.EnableNodeMode) return;
        if (_dispatcherQueue == null) return;
        
        try
        {
            Logger.Info("Initializing Windows Node service...");
            
            _nodeService = new NodeService(new AppLogger(), _dispatcherQueue, DataPath);
            _nodeService.StatusChanged += OnNodeStatusChanged;
            _nodeService.NotificationRequested += OnNodeNotificationRequested;
            _nodeService.PairingStatusChanged += OnPairingStatusChanged;
            
            // Connect to gateway as a node (separate connection from operator)
            _ = _nodeService.ConnectAsync(_settings.GatewayUrl, _settings.Token);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to initialize node service: {ex.Message}");
        }
    }
    
    private void OnNodeStatusChanged(object? sender, ConnectionStatus status)
    {
        Logger.Info($"Node status: {status}");
        
        // In node-only mode, surface node connection in main status indicator
        if (_settings?.EnableNodeMode == true)
        {
            _currentStatus = status;
            UpdateTrayIcon();
        }
        
        // Don't show "connected" toast if waiting for pairing - we'll show pairing status instead
        if (status == ConnectionStatus.Connected && _nodeService?.IsPaired == true)
        {
            try
            {
                new ToastContentBuilder()
                    .AddText("🔌 Node Mode Active")
                    .AddText("This PC can now receive commands from the agent (canvas, screenshots)")
                    .Show();
            }
            catch { /* ignore */ }
        }
    }
    
    private void OnPairingStatusChanged(object? sender, OpenClaw.Shared.PairingStatusEventArgs args)
    {
        Logger.Info($"Pairing status: {args.Status}");
        
        try
        {
            if (args.Status == OpenClaw.Shared.PairingStatus.Pending)
            {
                // Show toast with approval instructions
                new ToastContentBuilder()
                    .AddText("⏳ Awaiting Pairing Approval")
                    .AddText($"Run on gateway: openclaw devices approve {args.DeviceId.Substring(0, 16)}...")
                    .Show();
            }
            else if (args.Status == OpenClaw.Shared.PairingStatus.Paired)
            {
                new ToastContentBuilder()
                    .AddText("✅ Node Paired!")
                    .AddText("This PC can now receive commands from the agent")
                    .Show();
            }
        }
        catch { /* ignore */ }
    }
    
    private void OnNodeNotificationRequested(object? sender, OpenClaw.Shared.Capabilities.SystemNotifyArgs args)
    {
        // Agent requested a notification via node.invoke system.notify
        try
        {
            new ToastContentBuilder()
                .AddText(args.Title)
                .AddText(args.Body)
                .Show();
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to show node notification: {ex.Message}");
        }
    }

    private void OnConnectionStatusChanged(object? sender, ConnectionStatus status)
    {
        _currentStatus = status;
        UpdateTrayIcon();
        
        if (status == ConnectionStatus.Connected)
        {
            _ = RunHealthCheckAsync();
        }
    }

    private void OnActivityChanged(object? sender, AgentActivity? activity)
    {
        if (activity == null)
        {
            // Activity ended
            if (_displayedSessionKey != null && _sessionActivities.ContainsKey(_displayedSessionKey))
            {
                _sessionActivities.Remove(_displayedSessionKey);
            }
            _currentActivity = null;
        }
        else
        {
            var sessionKey = activity.SessionKey ?? "default";
            _sessionActivities[sessionKey] = activity;

            // Debounce session switching
            var now = DateTime.Now;
            if (_displayedSessionKey != sessionKey && 
                (now - _lastSessionSwitch) > SessionSwitchDebounce)
            {
                _displayedSessionKey = sessionKey;
                _lastSessionSwitch = now;
            }

            if (_displayedSessionKey == sessionKey)
            {
                _currentActivity = activity;
            }
        }
        
        UpdateTrayIcon();
    }

    private void OnChannelHealthUpdated(object? sender, ChannelHealth[] channels)
    {
        _lastChannels = channels;
    }

    private void OnSessionsUpdated(object? sender, SessionInfo[] sessions)
    {
        _lastSessions = sessions;
    }

    private void OnUsageUpdated(object? sender, GatewayUsageInfo usage)
    {
        _lastUsage = usage;
    }

    private void OnNotificationReceived(object? sender, OpenClawNotification notification)
    {
        if (_settings?.ShowNotifications != true) return;
        if (!ShouldShowNotification(notification)) return;

        // Store in history
        NotificationHistoryService.AddNotification(new Services.GatewayNotification
        {
            Title = notification.Title,
            Message = notification.Message,
            Category = notification.Type
        });

        // Show toast
        try
        {
            var builder = new ToastContentBuilder()
                .AddText(notification.Title ?? "OpenClaw")
                .AddText(notification.Message);

            // Add category-specific inline image (emoji rendered as text is fine, 
            // but we can add app logo override for better visibility)
            var logoPath = GetNotificationIcon(notification.Type);
            if (!string.IsNullOrEmpty(logoPath) && System.IO.File.Exists(logoPath))
            {
                builder.AddAppLogoOverride(new Uri(logoPath), ToastGenericAppLogoCrop.Circle);
            }

            // Add "Open Chat" button for chat notifications
            if (notification.IsChat)
            {
                builder.AddArgument("action", "open_chat")
                       .AddButton(new ToastButton()
                           .SetContent("Open Chat")
                           .AddArgument("action", "open_chat"));
            }

            builder.Show();
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to show toast: {ex.Message}");
        }
    }

    private static string? GetNotificationIcon(string? type)
    {
        // For now, use the app icon for all notifications
        // In the future, we could create category-specific icons
        var appDir = AppContext.BaseDirectory;
        var iconPath = System.IO.Path.Combine(appDir, "Assets", "claw.ico");
        return System.IO.File.Exists(iconPath) ? iconPath : null;
    }

    private bool ShouldShowNotification(OpenClawNotification notification)
    {
        if (_settings == null) return true;

        // Chat toggle: suppress all chat responses if disabled
        if (notification.IsChat && !_settings.NotifyChatResponses)
            return false;

        return notification.Type?.ToLowerInvariant() switch
        {
            "health" => _settings.NotifyHealth,
            "urgent" => _settings.NotifyUrgent,
            "reminder" => _settings.NotifyReminder,
            "email" => _settings.NotifyEmail,
            "calendar" => _settings.NotifyCalendar,
            "build" => _settings.NotifyBuild,
            "stock" => _settings.NotifyStock,
            "info" => _settings.NotifyInfo,
            "error" => _settings.NotifyUrgent, // errors follow urgent setting
            _ => true
        };
    }

    #endregion

    #region Health Check

    private void StartHealthCheckTimer()
    {
        _healthCheckTimer = new System.Timers.Timer(30000); // 30 seconds
        _healthCheckTimer.Elapsed += async (s, e) => await RunHealthCheckAsync();
        _healthCheckTimer.Start();

        _sessionPollTimer = new System.Timers.Timer(10000); // 10 seconds
        _sessionPollTimer.Elapsed += async (s, e) => await PollSessionsAsync();
        _sessionPollTimer.Start();

        // Initial check
        _ = RunHealthCheckAsync();
    }

    private async Task RunHealthCheckAsync()
    {
        if (_gatewayClient == null) return;

        try
        {
            _lastCheckTime = DateTime.Now;
            await _gatewayClient.CheckHealthAsync();
        }
        catch (Exception ex)
        {
            Logger.Warn($"Health check failed: {ex.Message}");
        }
    }

    private async Task PollSessionsAsync()
    {
        if (_gatewayClient == null) return;

        try
        {
            await _gatewayClient.RequestSessionsAsync();
            await _gatewayClient.RequestUsageAsync();
        }
        catch (Exception ex)
        {
            Logger.Warn($"Session poll failed: {ex.Message}");
        }
    }

    #endregion

    #region Tray Icon

    private void UpdateTrayIcon()
    {
        if (_trayIcon == null) return;

        var status = _currentStatus;
        if (_currentActivity != null && _currentActivity.Kind != OpenClaw.Shared.ActivityKind.Idle)
        {
            status = ConnectionStatus.Connecting; // Use connecting icon for activity
        }

        var iconPath = IconHelper.GetStatusIconPath(status);
        var tooltip = $"OpenClaw Tray — {_currentStatus}";
        
        if (_currentActivity != null && !string.IsNullOrEmpty(_currentActivity.DisplayText))
        {
            tooltip += $"\n{_currentActivity.DisplayText}";
        }

        tooltip += $"\nLast check: {_lastCheckTime:HH:mm:ss}";

        try
        {
            _trayIcon.SetIcon(iconPath);
            _trayIcon.Tooltip = tooltip;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to update tray icon: {ex.Message}");
        }
    }

    #endregion

    #region Window Management

    private void ShowSettings()
    {
        if (_settingsWindow == null || _settingsWindow.IsClosed)
        {
            _settingsWindow = new SettingsWindow(_settings!);
            _settingsWindow.Closed += (s, e) => 
            {
                _settingsWindow.SettingsSaved -= OnSettingsSaved;
                _settingsWindow = null;
            };
            _settingsWindow.SettingsSaved += OnSettingsSaved;
        }
        _settingsWindow.Activate();
    }

    private void OnSettingsSaved(object? sender, EventArgs e)
    {
        // Reconnect with new settings
        _gatewayClient?.Dispose();
        InitializeGatewayClient();
        
        // Reinitialize node service (safe dispose pattern)
        var oldNodeService = _nodeService;
        _nodeService = null;
        try { oldNodeService?.Dispose(); } catch (Exception ex) { Logger.Warn($"Node dispose error: {ex.Message}"); }
        InitializeNodeService();

        // Update global hotkey
        if (_settings!.GlobalHotkeyEnabled)
        {
            _globalHotkey ??= new GlobalHotkeyService();
            _globalHotkey.HotkeyPressed -= OnGlobalHotkeyPressed;
            _globalHotkey.HotkeyPressed += OnGlobalHotkeyPressed;
            _globalHotkey.Register();
        }
        else
        {
            _globalHotkey?.Unregister();
        }

        // Update auto-start
        AutoStartManager.SetAutoStart(_settings.AutoStart);
    }

    private void ShowWebChat()
    {
        if (_webChatWindow == null || _webChatWindow.IsClosed)
        {
            _webChatWindow = new WebChatWindow(_settings!.GatewayUrl, _settings.Token);
            _webChatWindow.Closed += (s, e) => _webChatWindow = null;
        }
        _webChatWindow.Activate();
    }

    private void ShowQuickSend(string? prefillMessage = null)
    {
        if (_gatewayClient == null) return;
        var dialog = new QuickSendDialog(_gatewayClient, prefillMessage);
        dialog.Activate();
    }

    private void ShowStatusDetail()
    {
        if (_statusDetailWindow == null || _statusDetailWindow.IsClosed)
        {
            _statusDetailWindow = new StatusDetailWindow(
                _currentStatus, _lastChannels, _lastSessions, _lastUsage, _lastCheckTime);
            _statusDetailWindow.Closed += (s, e) => _statusDetailWindow = null;
        }
        else
        {
            _statusDetailWindow.UpdateStatus(
                _currentStatus, _lastChannels, _lastSessions, _lastUsage, _lastCheckTime);
        }
        _statusDetailWindow.Activate();
    }

    private void ShowNotificationHistory()
    {
        if (_notificationHistoryWindow == null || _notificationHistoryWindow.IsClosed)
        {
            _notificationHistoryWindow = new NotificationHistoryWindow();
            _notificationHistoryWindow.Closed += (s, e) => _notificationHistoryWindow = null;
        }
        _notificationHistoryWindow.Activate();
    }

    private async Task ShowFirstRunWelcomeAsync()
    {
        var dialog = new WelcomeDialog();
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            ShowSettings();
        }
    }

    #endregion

    #region Actions

    private void OpenDashboard(string? path = null)
    {
        if (_settings == null) return;
        
        var baseUrl = _settings.GatewayUrl
            .Replace("ws://", "http://")
            .Replace("wss://", "https://");
        
        var url = string.IsNullOrEmpty(path) 
            ? $"{baseUrl}?token={Uri.EscapeDataString(_settings.Token)}"
            : $"{baseUrl}/{path}?token={Uri.EscapeDataString(_settings.Token)}";

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to open dashboard: {ex.Message}");
        }
    }

    private async void ToggleChannel(string channelName)
    {
        if (_gatewayClient == null) return;

        var channel = _lastChannels.FirstOrDefault(c => c.Name == channelName);
        if (channel == null) return;

        try
        {
            var isRunning = channel.Status?.ToLowerInvariant() is "ok" or "connected" or "running";
            if (isRunning)
            {
                await _gatewayClient.StopChannelAsync(channelName);
            }
            else
            {
                await _gatewayClient.StartChannelAsync(channelName);
            }
            
            // Refresh health
            await RunHealthCheckAsync();
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to toggle channel: {ex.Message}");
        }
    }

    private void ToggleAutoStart()
    {
        if (_settings == null) return;
        _settings.AutoStart = !_settings.AutoStart;
        _settings.Save();
        AutoStartManager.SetAutoStart(_settings.AutoStart);
    }

    private void OpenLogFile()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Logger.LogFilePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to open log file: {ex.Message}");
        }
    }

    private void OnGlobalHotkeyPressed(object? sender, EventArgs e)
    {
        ShowQuickSend();
    }

    #endregion

    #region Updates

    private async Task<bool> CheckForUpdatesAsync()
    {
        try
        {
            Logger.Info("Checking for updates...");
            var updateFound = await AppUpdater.CheckForUpdatesAsync();

            if (!updateFound)
            {
                Logger.Info("No updates available");
                return true;
            }

            var release = AppUpdater.LatestRelease!;
            var changelog = AppUpdater.GetChangelog(true) ?? "No release notes available.";
            Logger.Info($"Update available: {release.TagName}");

            var dialog = new UpdateDialog(release.TagName, changelog);
            var result = await dialog.ShowAsync();

            if (result == UpdateDialogResult.Download)
            {
                var installed = await DownloadAndInstallUpdateAsync();
                return !installed; // Don't launch if update succeeded
            }

            return true; // RemindLater or Skip - continue
        }
        catch (Exception ex)
        {
            Logger.Warn($"Update check failed: {ex.Message}");
            return true;
        }
    }

    private async Task<bool> DownloadAndInstallUpdateAsync()
    {
        DownloadProgressDialog? progressDialog = null;
        try
        {
            progressDialog = new DownloadProgressDialog(AppUpdater);
            progressDialog.ShowAsync(); // Fire and forget

            var downloadedAsset = await AppUpdater.DownloadUpdateAsync();

            progressDialog?.Close();

            if (downloadedAsset == null || !System.IO.File.Exists(downloadedAsset.FilePath))
            {
                Logger.Error("Update download failed or file missing");
                return false;
            }

            Logger.Info("Installing update and restarting...");
            await AppUpdater.InstallUpdateAsync(downloadedAsset);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"Update failed: {ex.Message}");
            progressDialog?.Close();
            return false;
        }
    }

    #endregion

    #region Deep Links

    private void StartDeepLinkServer()
    {
        _deepLinkCts = new CancellationTokenSource();
        var token = _deepLinkCts.Token;
        
        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In);
                    await pipe.WaitForConnectionAsync(token);
                    using var reader = new System.IO.StreamReader(pipe);
                    var uri = await reader.ReadLineAsync(token);
                    if (!string.IsNullOrEmpty(uri))
                    {
                        Logger.Info($"Received deep link via IPC: {uri}");
                        _dispatcherQueue?.TryEnqueue(() => HandleDeepLink(uri));
                    }
                }
                catch (OperationCanceledException)
                {
                    break; // Normal shutdown
                }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                    {
                        Logger.Warn($"Deep link server error: {ex.Message}");
                        try { await Task.Delay(1000, token); } catch { break; }
                    }
                }
            }
        }, token);
    }

    private void HandleDeepLink(string uri)
    {
        DeepLinkHandler.Handle(uri, new DeepLinkActions
        {
            OpenSettings = ShowSettings,
            OpenChat = ShowWebChat,
            OpenDashboard = OpenDashboard,
            OpenQuickSend = ShowQuickSend,
            SendMessage = async (msg) =>
            {
                if (_gatewayClient != null)
                {
                    await _gatewayClient.SendChatMessageAsync(msg);
                }
            }
        });
    }

    private static void SendDeepLinkToRunningInstance(string uri)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            pipe.Connect(1000);
            using var writer = new System.IO.StreamWriter(pipe);
            writer.WriteLine(uri);
            writer.Flush();
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to forward deep link: {ex.Message}");
        }
    }

    #endregion

    #region Toast Activation

    private void OnToastActivated(ToastNotificationActivatedEventArgsCompat args)
    {
        var arguments = ToastArguments.Parse(args.Argument);
        
        if (arguments.TryGetValue("action", out var action))
        {
            _dispatcherQueue?.TryEnqueue(() =>
            {
                switch (action)
                {
                    case "open_url" when arguments.TryGetValue("url", out var url):
                        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                        catch { }
                        break;
                    case "open_dashboard":
                        OpenDashboard();
                        break;
                    case "open_settings":
                        ShowSettings();
                        break;
                    case "open_chat":
                        ShowWebChat();
                        break;
                }
            });
        }
    }

    #endregion

    #region Exit

    private void ExitApplication()
    {
        Logger.Info("Application exiting");
        
        // Cancel background tasks
        _deepLinkCts?.Cancel();
        
        // Stop timers
        _healthCheckTimer?.Stop();
        _healthCheckTimer?.Dispose();
        _sessionPollTimer?.Stop();
        _sessionPollTimer?.Dispose();
        
        // Cleanup hotkey
        _globalHotkey?.Dispose();
        
        // Unsubscribe and dispose gateway client
        UnsubscribeGatewayEvents();
        _gatewayClient?.Dispose();
        
        // Dispose tray and mutex
        _trayIcon?.Dispose();
        _mutex?.Dispose();
        
        // Dispose cancellation token source
        _deepLinkCts?.Dispose();
        
        Exit();
    }

    #endregion

    private Microsoft.UI.Dispatching.DispatcherQueue? AppDispatcherQueue => 
        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
}

internal class AppLogger : IOpenClawLogger
{
    public void Info(string message) => Logger.Info(message);
    public void Warn(string message) => Logger.Warn(message);
    public void Error(string message, Exception? ex = null) => 
        Logger.Error(ex != null ? $"{message}: {ex.Message}" : message);
}
