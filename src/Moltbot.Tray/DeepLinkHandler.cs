using Microsoft.Win32;
using Moltbot.Shared;
using System;
using System.Collections.Specialized;
using System.Threading.Tasks;
using System.Web;
using System.Windows.Forms;

namespace MoltbotTray;

/// <summary>
/// Handles moltbot:// URI scheme registration and processing.
/// Matches macOS deep link support (moltbot://agent?message=...)
/// </summary>
public static class DeepLinkHandler
{
    private const string UriScheme = "Moltbot";
    private const string FriendlyName = "Moltbot Agent Command";

    /// <summary>
    /// Registers the moltbot:// URI scheme in the Windows registry.
    /// Requires elevation for HKCR, falls back to HKCU.
    /// </summary>
    public static void RegisterUriScheme()
    {
        try
        {
            var exePath = Environment.ProcessPath ?? Application.ExecutablePath;

            // Try HKCU\Software\Classes (no elevation needed)
            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{UriScheme}");
            if (key == null) return;

            key.SetValue("", $"URL:{FriendlyName}");
            key.SetValue("URL Protocol", "");

            using var iconKey = key.CreateSubKey("DefaultIcon");
            iconKey?.SetValue("", $"\"{exePath}\",1");

            using var commandKey = key.CreateSubKey(@"shell\open\command");
            commandKey?.SetValue("", $"\"{exePath}\" \"%1\"");

            Logger.Info($"Registered URI scheme: {UriScheme}://");
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to register URI scheme", ex);
        }
    }

    /// <summary>
    /// Checks if the app was launched with a deep link argument.
    /// </summary>
    public static bool TryGetDeepLink(string[] args, out Uri? uri)
    {
        uri = null;
        if (args.Length == 0) return false;

        foreach (var arg in args)
        {
            if (arg.StartsWith($"{UriScheme}://", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    uri = new Uri(arg);
                    return true;
                }
                catch { }
            }
        }
        return false;
    }

    /// <summary>
    /// Processes a moltbot:// deep link.
    /// Supports:
    ///   moltbot://agent?message=...
    ///   moltbot://send?message=...  (opens Quick Send with pre-filled text)
    ///   moltbot://dashboard
    ///   moltbot://chat
    ///   moltbot://settings
    /// </summary>
    public static async Task ProcessDeepLinkAsync(Uri uri, MoltbotGatewayClient client, Action<string>? openDashboard = null, Action? openChat = null, Action? openSettings = null, Action<string>? openQuickSend = null)
    {
        Logger.Info($"Processing deep link: {uri}");

        var host = uri.Host.ToLowerInvariant();
        var query = HttpUtility.ParseQueryString(uri.Query);

        switch (host)
        {
            case "agent":
                await HandleAgentDeepLinkAsync(query, client);
                break;
            case "send":
                var msg = query["message"] ?? "";
                openQuickSend?.Invoke(msg);
                break;
            case "dashboard":
                openDashboard?.Invoke(uri.AbsolutePath.TrimStart('/'));
                break;
            case "chat":
                openChat?.Invoke();
                break;
            case "settings":
                openSettings?.Invoke();
                break;
            default:
                Logger.Warn($"Unknown deep link host: {host}");
                break;
        }
    }

    private static async Task HandleAgentDeepLinkAsync(NameValueCollection query, MoltbotGatewayClient client)
    {
        var message = query["message"];
        if (string.IsNullOrWhiteSpace(message))
        {
            Logger.Warn("Deep link: missing message parameter");
            return;
        }

        var key = query["key"];
        var hasKey = !string.IsNullOrEmpty(key);

        // Without a key, prompt for confirmation (safety)
        if (!hasKey)
        {
            var preview = message.Length > 100 ? message[..100] + "…" : message;
            var result = MessageBox.Show(
                $"A deep link wants to send this message to Moltbot:\n\n\"{preview}\"\n\nAllow?",
                "Moltbot Deep Link",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
            {
                Logger.Info("Deep link: user declined");
                return;
            }
        }

        try
        {
            await client.SendChatMessageAsync(message);
            Logger.Info($"Deep link: sent message ({message.Length} chars)");
            
            // Show confirmation toast
            try
            {
                new Microsoft.Toolkit.Uwp.Notifications.ToastContentBuilder()
                    .AddText("🦞 Message Sent")
                    .AddText(message.Length > 50 ? message[..50] + "…" : message)
                    .Show();
            }
            catch { }
        }
        catch (Exception ex)
        {
            Logger.Error("Deep link: failed to send", ex);
            
            // Show error toast
            try
            {
                new Microsoft.Toolkit.Uwp.Notifications.ToastContentBuilder()
                    .AddText("❌ Failed to Send")
                    .AddText(ex.Message)
                    .Show();
            }
            catch { }
        }
    }
}

