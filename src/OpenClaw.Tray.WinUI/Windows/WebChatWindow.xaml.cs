using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using OpenClaw.Shared;
using OpenClawTray.Helpers;
using OpenClawTray.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using WinUIEx;
using Windows.Foundation;

namespace OpenClawTray.Windows;

public sealed partial class WebChatWindow : WindowEx
{
    private readonly string _gatewayUrl;
    private readonly string _token;
    
    // Store event handlers for cleanup
    private TypedEventHandler<CoreWebView2, CoreWebView2NavigationCompletedEventArgs>? _navigationCompletedHandler;
    private TypedEventHandler<CoreWebView2, CoreWebView2NavigationStartingEventArgs>? _navigationStartingHandler;
    
    public bool IsClosed { get; private set; }

    public WebChatWindow(string gatewayUrl, string token)
    {
        Logger.Info($"WebChatWindow: Constructor called, gateway={gatewayUrl}");
        _gatewayUrl = gatewayUrl;
        _token = token;
        
        InitializeComponent();
        
        // Window configuration
        this.SetWindowSize(520, 750);
        this.MinWidth = 380;
        this.MinHeight = 450;
        this.CenterOnScreen();
        this.SetIcon(IconHelper.GetStatusIconPath(ConnectionStatus.Connected));
        
        Closed += OnWindowClosed;
        
        Logger.Info("WebChatWindow: Starting InitializeWebViewAsync");
        _ = InitializeWebViewAsync();
    }

    private void OnWindowClosed(object sender, WindowEventArgs e)
    {
        IsClosed = true;
        
        // Cleanup WebView2 event handlers
        if (WebView.CoreWebView2 != null)
        {
            if (_navigationCompletedHandler != null)
                WebView.CoreWebView2.NavigationCompleted -= _navigationCompletedHandler;
            if (_navigationStartingHandler != null)
                WebView.CoreWebView2.NavigationStarting -= _navigationStartingHandler;
        }
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            Logger.Info("WebChatWindow: Initializing WebView2...");
            
            // Set up user data folder for WebView2
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenClawTray", "WebView2");
            
            Directory.CreateDirectory(userDataFolder);
            Logger.Info($"WebChatWindow: User data folder: {userDataFolder}");

            // Set environment variable for user data folder
            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", userDataFolder);
            
            Logger.Info("WebChatWindow: Calling EnsureCoreWebView2Async...");
            await WebView.EnsureCoreWebView2Async();
            Logger.Info("WebChatWindow: CoreWebView2 initialized successfully");
            
            // Configure WebView2
            WebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            WebView.CoreWebView2.Settings.IsZoomControlEnabled = true;

            // Handle navigation events (store for cleanup)
            _navigationCompletedHandler = (s, e) =>
            {
                Logger.Info($"WebChatWindow: Navigation completed, success={e.IsSuccess}, status={e.WebErrorStatus}");
                LoadingRing.IsActive = false;
                LoadingRing.Visibility = Visibility.Collapsed;
                
                // Show friendly error if connection failed
                if (!e.IsSuccess && (e.WebErrorStatus == CoreWebView2WebErrorStatus.ConnectionAborted ||
                                      e.WebErrorStatus == CoreWebView2WebErrorStatus.CannotConnect ||
                                      e.WebErrorStatus == CoreWebView2WebErrorStatus.ConnectionReset ||
                                      e.WebErrorStatus == CoreWebView2WebErrorStatus.ServerUnreachable))
                {
                    Logger.Info("WebChatWindow: Gateway unreachable, showing friendly error");
                    WebView.Visibility = Visibility.Collapsed;
                    ErrorPanel.Visibility = Visibility.Visible;
                    ErrorText.Text = "Can't reach OpenClaw Gateway\n\n" +
                        $"The gateway at {_gatewayUrl} is not responding.\n\n" +
                        "To connect:\n" +
                        "• Make sure your OpenClaw gateway is running\n" +
                        "• If remote, connect via VPN to your home network\n" +
                        "• Or use SSH tunnel: ssh -N -L 18789:localhost:18789 your-server";
                }
            };
            WebView.CoreWebView2.NavigationCompleted += _navigationCompletedHandler;

            _navigationStartingHandler = (s, e) =>
            {
                Logger.Info($"WebChatWindow: Navigation starting to {e.Uri}");
                LoadingRing.IsActive = true;
                LoadingRing.Visibility = Visibility.Visible;
            };
            WebView.CoreWebView2.NavigationStarting += _navigationStartingHandler;

            // Navigate to chat
            NavigateToChat();
        }
        catch (Exception ex)
        {
            Logger.Error($"WebView2 initialization failed: {ex.GetType().FullName}: {ex.Message}");
            Logger.Error($"WebView2 HResult: 0x{ex.HResult:X8}");
            if (ex.InnerException != null)
            {
                Logger.Error($"WebView2 inner exception: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}");
            }
            Logger.Error($"WebView2 stack trace: {ex.StackTrace}");
            
            // Show error in the dialog instead of falling back to browser
            LoadingRing.IsActive = false;
            LoadingRing.Visibility = Visibility.Collapsed;
            WebView.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Visible;
            
            var errorDetails = $"Exception: {ex.GetType().FullName}\n" +
                              $"HResult: 0x{ex.HResult:X8}\n" +
                              $"Message: {ex.Message}\n\n" +
                              $"App Directory: {AppContext.BaseDirectory}\n" +
                              $"Architecture: {RuntimeInformation.ProcessArchitecture}\n" +
                              $"OS: {RuntimeInformation.OSDescription}\n\n" +
                              $"Stack Trace:\n{ex.StackTrace}";
            
            if (ex.InnerException != null)
            {
                errorDetails += $"\n\nInner Exception: {ex.InnerException.GetType().FullName}\n{ex.InnerException.Message}";
            }
            
            ErrorText.Text = errorDetails;
        }
    }

    // Set to a test URL to bypass gateway (e.g., "https://www.bing.com"), or null for normal operation
    private const string? DEBUG_TEST_URL = null;
    
    private void NavigateToChat()
    {
        if (WebView.CoreWebView2 == null) return;

        // If debug URL is set, use it instead of gateway
        if (!string.IsNullOrEmpty(DEBUG_TEST_URL))
        {
            Logger.Info($"WebChatWindow: DEBUG MODE - Navigating to test URL: {DEBUG_TEST_URL}");
            WebView.CoreWebView2.Navigate(DEBUG_TEST_URL);
            return;
        }

        var baseUrl = _gatewayUrl
            .Replace("ws://", "http://")
            .Replace("wss://", "https://");
        
        var url = $"{baseUrl}?token={Uri.EscapeDataString(_token)}";
        Logger.Info($"WebChatWindow: Navigating to {baseUrl} (token hidden)");
        WebView.CoreWebView2.Navigate(url);
    }

    private void OnHome(object sender, RoutedEventArgs e)
    {
        NavigateToChat();
    }

    private void OnRefresh(object sender, RoutedEventArgs e)
    {
        WebView.CoreWebView2?.Reload();
    }

    private void OnPopout(object sender, RoutedEventArgs e)
    {
        var baseUrl = _gatewayUrl
            .Replace("ws://", "http://")
            .Replace("wss://", "https://");
        var url = $"{baseUrl}?token={Uri.EscapeDataString(_token)}";
        
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to open in browser: {ex.Message}");
        }
    }

    private void OnDevTools(object sender, RoutedEventArgs e)
    {
        WebView.CoreWebView2?.OpenDevToolsWindow();
    }
}
