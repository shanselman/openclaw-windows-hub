using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Runtime.InteropServices;

namespace OpenClawTray.Windows;

/// <summary>
/// A minimal, invisible window used as an anchor point for displaying MenuFlyout from tray icon.
/// 
/// BACKGROUND:
/// WinUI 3's MenuFlyout.ShowAt() requires a valid UIElement with proper visual tree context.
/// TrayIcon doesn't provide this context, causing crashes in Microsoft.UI.Windowing.Core.dll.
/// 
/// SOLUTION:
/// This 1x1 pixel window is positioned at the cursor and provides the required anchor.
/// It's created once and reused to avoid creation/destruction overhead.
/// A strong reference is maintained in App to prevent garbage collection.
/// 
/// See TRAY_MENU_CRASH_FIX.md for detailed documentation.
/// </summary>
public sealed partial class TrayMenuAnchorWindow : Window
{
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

    public TrayMenuAnchorWindow()
    {
        InitializeComponent();
        
        // Configure window to be invisible but present
        this.AppWindow.IsShownInSwitchers = false;
        this.ExtendsContentIntoTitleBar = true;
        this.SystemBackdrop = null; // No backdrop effect
        
        // Set to 1x1 size - minimal footprint
        this.AppWindow.Resize(new Windows.Graphics.SizeInt32(1, 1));
    }

    /// <summary>
    /// Positions the anchor window at the cursor location (for tray menu positioning)
    /// </summary>
    public void PositionAtCursor(int x, int y)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        
        // Position window at cursor location, topmost, without activating
        SetWindowPos(hwnd, HWND_TOPMOST, x, y, 1, 1, SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    /// <summary>
    /// Positions the anchor window off-screen (hidden but still valid for anchoring)
    /// </summary>
    public void PositionOffscreen()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        
        // Move far off-screen where it won't be visible
        int screenWidth = GetSystemMetrics(SM_CXSCREEN);
        int screenHeight = GetSystemMetrics(SM_CYSCREEN);
        
        SetWindowPos(hwnd, HWND_TOPMOST, screenWidth + 100, screenHeight + 100, 1, 1, SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    /// <summary>
    /// Shows the flyout anchored to this window's root grid
    /// </summary>
    public void ShowFlyout(MenuFlyout flyout)
    {
        if (flyout == null)
            throw new ArgumentNullException(nameof(flyout));

        // Ensure the flyout closes when it loses focus
        flyout.Closed += (s, e) => this.Hide();

        // Show flyout anchored to the root grid of this window
        flyout.ShowAt(RootGrid);
    }

    /// <summary>
    /// Hides the window (keeps it in memory for reuse)
    /// </summary>
    public void Hide()
    {
        try
        {
            // Move offscreen instead of truly hiding to keep it valid as an anchor
            PositionOffscreen();
        }
        catch
        {
            // Ignore errors during hide
        }
    }
}
