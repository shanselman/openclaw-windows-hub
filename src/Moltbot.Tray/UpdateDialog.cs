using System;
using System.Drawing;
using System.Windows.Forms;

namespace MoltbotTray;

public enum UpdateDialogResult
{
    Download,
    RemindLater,
    Skip
}

public class UpdateDialog : Form
{
    public UpdateDialogResult Result { get; private set; } = UpdateDialogResult.RemindLater;

    public UpdateDialog(string version, string releaseNotes)
    {
        Text = "Update Available - Moltbot Tray";
        Size = new Size(500, 400);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Icon = SystemIcons.Information;

        var titleLabel = new Label
        {
            Text = "🦞 Update Available!",
            Font = new Font(Font.FontFamily, 14, FontStyle.Bold),
            Location = new Point(20, 20),
            AutoSize = true
        };
        Controls.Add(titleLabel);

        var versionLabel = new Label
        {
            Text = $"Version {version} is ready to install",
            Location = new Point(20, 55),
            AutoSize = true
        };
        Controls.Add(versionLabel);

        var notesLabel = new Label
        {
            Text = "Release Notes:",
            Font = new Font(Font.FontFamily, 9, FontStyle.Bold),
            Location = new Point(20, 85),
            AutoSize = true
        };
        Controls.Add(notesLabel);

        var notesBox = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(releaseNotes) ? "No release notes available." : releaseNotes,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Location = new Point(20, 110),
            Size = new Size(440, 180),
            BackColor = SystemColors.Window
        };
        Controls.Add(notesBox);

        var downloadButton = new Button
        {
            Text = "Download && Install",
            Size = new Size(130, 35),
            Location = new Point(20, 310),
            Font = new Font(Font.FontFamily, 9, FontStyle.Bold)
        };
        downloadButton.Click += (_, _) =>
        {
            Result = UpdateDialogResult.Download;
            DialogResult = DialogResult.OK;
            Close();
        };
        Controls.Add(downloadButton);

        var remindButton = new Button
        {
            Text = "Remind Me Later",
            Size = new Size(130, 35),
            Location = new Point(170, 310)
        };
        remindButton.Click += (_, _) =>
        {
            Result = UpdateDialogResult.RemindLater;
            DialogResult = DialogResult.Cancel;
            Close();
        };
        Controls.Add(remindButton);

        var skipButton = new Button
        {
            Text = "Skip This Version",
            Size = new Size(130, 35),
            Location = new Point(320, 310)
        };
        skipButton.Click += (_, _) =>
        {
            Result = UpdateDialogResult.Skip;
            DialogResult = DialogResult.Cancel;
            Close();
        };
        Controls.Add(skipButton);

        AcceptButton = downloadButton;
        CancelButton = remindButton;
    }
}
