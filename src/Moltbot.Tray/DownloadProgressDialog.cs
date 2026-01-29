using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Updatum;

namespace MoltbotTray;

public class DownloadProgressDialog : Form
{
    private readonly UpdatumManager _updater;
    private readonly ProgressBar _progressBar;
    private readonly Label _progressLabel;

    public DownloadProgressDialog(UpdatumManager updater)
    {
        _updater = updater;
        _updater.PropertyChanged += UpdaterOnPropertyChanged;

        Text = "Downloading Update - Moltbot Tray";
        Size = new Size(400, 150);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false; // No close button during download
        Icon = SystemIcons.Information;

        var titleLabel = new Label
        {
            Text = "🦞 Downloading update...",
            Font = new Font(Font.FontFamily, 10, FontStyle.Bold),
            Location = new Point(20, 20),
            AutoSize = true
        };
        Controls.Add(titleLabel);

        _progressBar = new ProgressBar
        {
            Location = new Point(20, 55),
            Size = new Size(340, 25),
            Minimum = 0,
            Maximum = 100,
            Style = ProgressBarStyle.Continuous
        };
        Controls.Add(_progressBar);

        _progressLabel = new Label
        {
            Text = "Starting download...",
            Location = new Point(20, 85),
            Size = new Size(340, 20),
            TextAlign = ContentAlignment.MiddleCenter
        };
        Controls.Add(_progressLabel);
    }

    private void UpdaterOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UpdatumManager.DownloadedPercentage))
        {
            if (InvokeRequired)
            {
                Invoke(() => UpdateProgress());
            }
            else
            {
                UpdateProgress();
            }
        }
    }

    private void UpdateProgress()
    {
        _progressBar.Value = (int)Math.Min(_updater.DownloadedPercentage, 100);
        _progressLabel.Text = $"{_updater.DownloadedMegabytes:F2} MB / {_updater.DownloadSizeMegabytes:F2} MB ({_updater.DownloadedPercentage:F1}%)";
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _updater.PropertyChanged -= UpdaterOnPropertyChanged;
        base.OnFormClosing(e);
    }
}
