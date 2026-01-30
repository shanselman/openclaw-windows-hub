using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using OpenClawTray.Helpers;
using System;
using System.Runtime.InteropServices;
using WinUIEx;

namespace OpenClawTray.Windows;

/// <summary>
/// A popup window that displays the tray menu at the cursor position.
/// </summary>
public sealed partial class TrayMenuWindow : WindowEx
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    public event EventHandler<string>? MenuItemClicked;

    private int _menuHeight = 400;
    private int _itemCount = 0;
    private int _separatorCount = 0;
    private int _headerCount = 0;

    public TrayMenuWindow()
    {
        InitializeComponent();

        // Configure as popup-style window
        this.IsMaximizable = false;
        this.IsMinimizable = false;
        this.IsResizable = false;
        this.IsTitleBarVisible = false;
        this.IsAlwaysOnTop = true;
        
        // Lose focus = close
        Activated += OnActivated;
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            Close();
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    public void ShowAtCursor()
    {
        if (GetCursorPos(out POINT pt))
        {
            // Get DPI scale factor
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            uint dpi = GetDpiForWindow(hwnd);
            if (dpi == 0) dpi = 96;
            double scale = dpi / 96.0;

            // Get the monitor where the cursor is
            var hMonitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
            var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            GetMonitorInfo(hMonitor, ref monitorInfo);

            var workArea = monitorInfo.rcWork;
            
            // Scale the menu dimensions to physical pixels
            int menuWidthPhysical = (int)(280 * scale);
            int menuHeightPhysical = (int)(_menuHeight * scale);
            
            // Calculate X position - keep menu on screen
            int x = pt.X;
            if (x + menuWidthPhysical > workArea.Right)
                x = workArea.Right - menuWidthPhysical;
            if (x < workArea.Left)
                x = workArea.Left;

            // Calculate Y position - open ABOVE cursor (tray is at bottom)
            int y = pt.Y - menuHeightPhysical - 10;
            
            // If not enough room above, open below
            if (y < workArea.Top)
                y = pt.Y + 10;

            this.Move(x, y);
        }

        Activate();

        // Ensure window gets focus so clicking away will close it
        var hwndFocus = WinRT.Interop.WindowNative.GetWindowHandle(this);
        SetForegroundWindow(hwndFocus);
    }

    public void AddMenuItem(string text, string? icon, string action, bool isEnabled = true, bool indent = false)
    {
        var content = new TextBlock
        {
            Text = string.IsNullOrEmpty(icon) ? text : $"{icon}  {text}",
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsTextSelectionEnabled = false
        };

        var leftPadding = indent ? 28 : 12;
        var button = new Button
        {
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(leftPadding, 8, 12, 8),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            IsEnabled = isEnabled,
            Tag = action,
            CornerRadius = new CornerRadius(4)
        };

        if (!isEnabled)
            content.Opacity = 0.5;

        button.Click += (s, e) =>
        {
            MenuItemClicked?.Invoke(this, action);
            Close();
        };

        // Hover effect
        button.PointerEntered += (s, e) =>
        {
            if (button.IsEnabled)
                button.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
        };
        button.PointerExited += (s, e) =>
        {
            button.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        };

        MenuPanel.Children.Add(button);
        _itemCount++;
    }

    public void AddSeparator()
    {
        MenuPanel.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(8, 6, 8, 6),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"]
        });
        _separatorCount++;
    }

    public void AddBrandHeader(string emoji, string text)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Padding = new Thickness(12, 12, 12, 8),
            Spacing = 8
        };

        panel.Children.Add(new TextBlock
        {
            Text = emoji,
            FontSize = 28
        });

        panel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });

        MenuPanel.Children.Add(panel);
        _headerCount += 2; // Counts as larger
    }

    public void AddHeader(string text)
    {
        MenuPanel.Children.Add(new TextBlock
        {
            Text = text,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Padding = new Thickness(12, 10, 12, 4),
            Opacity = 0.7
        });
        _headerCount++;
    }

    public void ClearItems()
    {
        MenuPanel.Children.Clear();
        _itemCount = 0;
        _separatorCount = 0;
        _headerCount = 0;
    }

    /// <summary>
    /// Adjusts the window height to fit content and stores it for positioning
    /// </summary>
    public void SizeToContent()
    {
        // Calculate height based on item counts
        // Menu items: ~36px each (button with padding)
        // Separators: ~13px each  
        // Headers: ~30px each
        // Plus padding: ~16px
        _menuHeight = (_itemCount * 36) + (_separatorCount * 13) + (_headerCount * 30) + 16;
        _menuHeight = Math.Max(_menuHeight, 100); // minimum
        this.SetWindowSize(280, _menuHeight);
    }
}
