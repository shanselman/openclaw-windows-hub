using MoltbotTray;
using System;
using System.Diagnostics;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Updatum;

namespace MoltbotTray;

internal static class Program
{
    private const string PipeName = "MoltbotTray-DeepLink";
    
    internal static readonly UpdatumManager AppUpdater = new("shanselman", "moltbot-windows-hub")
    {
        FetchOnlyLatestRelease = true,
        InstallUpdateSingleFileExecutableName = "Moltbot.Tray",
    };

    [STAThread]
    static void Main(string[] args)
    {
        // Single instance check
        using var mutex = new Mutex(true, "MoltbotTray", out bool createdNew);
        if (!createdNew)
        {
            // Forward deep link args to running instance via named pipe
            if (args.Length > 0 && args[0].StartsWith("moltbot://", StringComparison.OrdinalIgnoreCase))
            {
                SendDeepLinkToRunningInstance(args[0]);
            }
            else
            {
                MessageBox.Show("Moltbot Tray is already running.", "Moltbot Tray",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return;
        }

        // Register URI scheme on first run
        DeepLinkHandler.RegisterUriScheme();

        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Check for updates before launching
        var shouldLaunch = CheckForUpdatesAsync().GetAwaiter().GetResult();
        
        if (shouldLaunch)
        {
            var trayApp = new TrayApplication(args);
            Application.Run(trayApp);
        }
    }

    private static async Task<bool> CheckForUpdatesAsync()
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

            using var dialog = new UpdateDialog(release.TagName, changelog);
            dialog.ShowDialog();

            if (dialog.Result == UpdateDialogResult.Download)
            {
                var installed = await DownloadAndInstallUpdateAsync();
                // If install succeeded, app will restart - don't launch
                // If install failed, let user continue to the app
                return !installed;
            }

            // RemindLater or Skip - continue to launch
            return true;
        }
        catch (Exception ex)
        {
            // Update check failed - don't block the app, just launch
            Logger.Warn($"Update check failed: {ex.Message}");
            return true;
        }
    }

    private static async Task<bool> DownloadAndInstallUpdateAsync()
    {
        DownloadProgressDialog? progressDialog = null;
        try
        {
            progressDialog = new DownloadProgressDialog(AppUpdater);
            progressDialog.Show();

            var downloadedAsset = await AppUpdater.DownloadUpdateAsync();

            progressDialog?.Close();
            progressDialog = null;

            if (downloadedAsset == null)
            {
                MessageBox.Show("Failed to download the update. Please try again later.",
                    "Download Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            if (!System.IO.File.Exists(downloadedAsset.FilePath))
            {
                MessageBox.Show($"Update file was deleted or is inaccessible:\n{downloadedAsset.FilePath}\n\nThis may be caused by antivirus software.",
                    "Update File Missing", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            var confirmResult = MessageBox.Show(
                "The update has been downloaded. Moltbot Tray will now restart to install the update.\n\nContinue?",
                "Install Update",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirmResult == DialogResult.Yes)
            {
                Logger.Info("Installing update and restarting...");
                await AppUpdater.InstallUpdateAsync(downloadedAsset);
                return true; // App will restart
            }

            return false; // User cancelled, continue to app
        }
        catch (UnauthorizedAccessException ex)
        {
            MessageBox.Show($"Access denied when accessing update file.\n\n1. Antivirus may be blocking the update\n2. Windows SmartScreen may need approval\n\nError: {ex.Message}",
                "Access Denied", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error($"Update download/install failed: {ex.Message}");
            MessageBox.Show($"Failed to download or install update: {ex.Message}",
                "Update Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        finally
        {
            progressDialog?.Close();
        }
    }

    private static void SendDeepLinkToRunningInstance(string uri)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            pipe.Connect(1000); // 1 second timeout
            using var writer = new System.IO.StreamWriter(pipe);
            writer.WriteLine(uri);
            writer.Flush();
            Logger.Info($"Forwarded deep link to running instance: {uri}");
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to forward deep link: {ex.Message}");
            MessageBox.Show($"Moltbot Tray is running but couldn't process the deep link.\n\nPlease try again.",
                "Deep Link Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    internal static void StartDeepLinkServer(Action<string> onDeepLinkReceived)
    {
        Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In);
                    await pipe.WaitForConnectionAsync();
                    using var reader = new System.IO.StreamReader(pipe);
                    var uri = await reader.ReadLineAsync();
                    if (!string.IsNullOrEmpty(uri))
                    {
                        Logger.Info($"Received deep link via IPC: {uri}");
                        onDeepLinkReceived(uri);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Deep link server error: {ex.Message}");
                    await Task.Delay(1000);
                }
            }
        });
    }
}

