using System;
using System.Drawing;
using System.Windows.Forms;

namespace MoltbotTray;

public partial class QuickSendDialog : ModernForm
{
    private TextBox _messageTextBox = null!;
    private Button _sendButton = null!;
    private Button _cancelButton = null!;
    private Label _hintLabel = null!;

    public string Message => _messageTextBox.Text;

    public QuickSendDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text = "Quick Send — Moltbot";
        Size = new Size(520, 300);
        ShowInTaskbar = true;
        TopMost = true;
        Icon = IconHelper.GetLobsterIcon();

        // Header label
        var label = CreateModernLabel("Send a message to Moltbot:");
        label.Location = new Point(20, 20);
        label.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
        label.ForeColor = AccentColor;

        // Message text box
        _messageTextBox = CreateModernTextBox();
        _messageTextBox.Location = new Point(20, 52);
        _messageTextBox.Size = new Size(464, 110);
        _messageTextBox.Multiline = true;
        _messageTextBox.ScrollBars = ScrollBars.Vertical;
        _messageTextBox.AcceptsReturn = false;
        _messageTextBox.Font = new Font("Segoe UI", 10.5f);

        // Buttons row (below text box)
        _sendButton = CreateModernButton("Send", isPrimary: true);
        _sendButton.Location = new Point(394, 172);
        _sendButton.Size = new Size(90, 32);
        _sendButton.Click += OnSendClick;

        _cancelButton = CreateModernButton("Cancel");
        _cancelButton.Location = new Point(296, 172);
        _cancelButton.Size = new Size(90, 32);
        _cancelButton.Click += OnCancelClick;

        // Hint label (below buttons with more space)
        _hintLabel = CreateModernLabel("Enter to send · Esc to cancel · Shift+Enter for new line", isSubtle: true);
        _hintLabel.Location = new Point(20, 220);
        _hintLabel.Font = new Font("Segoe UI", 8.5F);

        AcceptButton = _sendButton;
        CancelButton = _cancelButton;

        Controls.AddRange(new Control[] { label, _messageTextBox, _sendButton, _cancelButton, _hintLabel });

        Shown += (_, _) =>
        {
            _messageTextBox.Focus();
            Activate();
        };
    }

    private void OnSendClick(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_messageTextBox.Text))
        {
            _messageTextBox.Focus();
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnCancelClick(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    public void SetMessage(string message)
    {
        _messageTextBox.Text = message;
        _messageTextBox.SelectionStart = message.Length;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Enter) || keyData == Keys.Enter)
        {
            OnSendClick(null, EventArgs.Empty);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}

