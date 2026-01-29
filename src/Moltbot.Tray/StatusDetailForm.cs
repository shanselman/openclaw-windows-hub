using Moltbot.Shared;
using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace MoltbotTray;

/// <summary>
/// Shows detailed gateway status, sessions, channels, and usage in a rich view.
/// </summary>
public class StatusDetailForm : Form
{
    private RichTextBox _textBox = null!;
    private Button _refreshButton = null!;
    private Button _closeButton = null!;
    private readonly MoltbotGatewayClient? _client;
    private readonly SettingsManager? _settings;
    private readonly ConnectionStatus _status;

    private static StatusDetailForm? _instance;

    public static void ShowOrFocus(MoltbotGatewayClient? client, SettingsManager? settings, ConnectionStatus status)
    {
        if (_instance != null && !_instance.IsDisposed)
        {
            _instance.BringToFront();
            _instance.Focus();
            return;
        }

        _instance = new StatusDetailForm(client, settings, status);
        _instance.Show();
    }

    private StatusDetailForm(MoltbotGatewayClient? client, SettingsManager? settings, ConnectionStatus status)
    {
        _client = client;
        _settings = settings;
        _status = status;
        InitializeComponent();
        RefreshStatus();
    }

    private void InitializeComponent()
    {
        Text = "Moltbot Status";
        Size = new Size(520, 500);
        MinimumSize = new Size(400, 350);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = IconHelper.GetLobsterIcon();

        _textBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Font = new Font("Cascadia Code", 10F, FontStyle.Regular, GraphicsUnit.Point),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.FromArgb(220, 220, 220),
            BorderStyle = BorderStyle.None,
            WordWrap = true
        };

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(5)
        };

        _closeButton = new Button
        {
            Text = "&Close",
            Size = new Size(75, 26),
            Font = new Font("Segoe UI", 9F)
        };
        _closeButton.Click += (_, _) => Close();

        _refreshButton = new Button
        {
            Text = "&Refresh",
            Size = new Size(75, 26),
            Font = new Font("Segoe UI", 9F)
        };
        _refreshButton.Click += async (_, _) =>
        {
            if (_client != null)
            {
                await _client.CheckHealthAsync();
                await _client.RequestSessionsAsync();
                await _client.RequestUsageAsync();
            }
            RefreshStatus();
        };

        buttonPanel.Controls.Add(_closeButton);
        buttonPanel.Controls.Add(_refreshButton);

        Controls.Add(_textBox);
        Controls.Add(buttonPanel);
    }

    private void RefreshStatus()
    {
        var sb = new StringBuilder();

        // Header
        sb.AppendLine("⚡ MOLTBOT STATUS");
        sb.AppendLine(new string('─', 40));
        sb.AppendLine();

        // Connection
        var statusIcon = _status switch
        {
            ConnectionStatus.Connected => "🟢",
            ConnectionStatus.Connecting => "🟡",
            ConnectionStatus.Error => "🔴",
            _ => "⚪"
        };
        sb.AppendLine($"  Gateway:  {statusIcon} {_status}");
        sb.AppendLine($"  URL:      {_settings?.GatewayUrl ?? "not configured"}");
        sb.AppendLine($"  Token:    {(_settings?.Token?.Length > 0 ? "••••••••" : "not set")}");
        sb.AppendLine();

        // Sessions
        if (_client != null)
        {
            var sessions = _client.GetSessionList();
            if (sessions.Length > 0)
            {
                sb.AppendLine("🧠 SESSIONS");
                sb.AppendLine(new string('─', 40));
                foreach (var s in sessions)
                {
                    sb.AppendLine($"  {s.DisplayText}");
                    if (s.Model != null)
                        sb.AppendLine($"    Model: {s.Model}");
                    if (s.StartedAt != null)
                        sb.AppendLine($"    Started: {s.StartedAt:HH:mm:ss}");
                }
                sb.AppendLine();
            }
        }

        // App info
        sb.AppendLine("ℹ️  APP INFO");
        sb.AppendLine(new string('─', 40));
        sb.AppendLine($"  Version:  1.0.0");
        sb.AppendLine($"  Runtime:  {Environment.Version}");
        sb.AppendLine($"  OS:       {Environment.OSVersion}");
        sb.AppendLine($"  Machine:  {Environment.MachineName}");
        sb.AppendLine($"  PID:      {Environment.ProcessId}");
        sb.AppendLine($"  Uptime:   {GetUptime()}");
        sb.AppendLine();

        // Auto-start
        sb.AppendLine("⚙️  SETTINGS");
        sb.AppendLine(new string('─', 40));
        sb.AppendLine($"  Auto-start:     {(_settings?.AutoStart == true ? "✅" : "❌")}");
        sb.AppendLine($"  Notifications:  {(_settings?.ShowNotifications == true ? "✅" : "❌")}");
        sb.AppendLine($"  Sound:          {_settings?.NotificationSound ?? "Default"}");

        _textBox.Text = sb.ToString();
    }

    private static string GetUptime()
    {
        var elapsed = DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime;
        if (elapsed.TotalHours >= 1)
            return $"{elapsed.Hours}h {elapsed.Minutes}m";
        if (elapsed.TotalMinutes >= 1)
            return $"{elapsed.Minutes}m {elapsed.Seconds}s";
        return $"{elapsed.Seconds}s";
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _instance = null;
        base.OnFormClosed(e);
    }
}

