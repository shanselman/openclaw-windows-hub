# Moltbot Windows Tray

A Windows system tray companion for [Moltbot](https://github.com/Moltbot/Moltbot) — the Windows equivalent of the macOS menu bar app. Provides desktop notifications, embedded chat, live agent activity monitoring, and gateway status tracking.

## Features

### System Tray
- **Lobster icon** 🦞 when connected (pixel art), color-coded circles for other states
- **Activity badge** showing what the agent is doing (exec, read, write, edit, search, browser, message, tool)
- **Context menu** with status, sessions, channels, usage, quick send, settings, and auto-start
- **Text status labels** `[ON]`/`[OFF]`/`[READY]`/`[LINKED]` for clarity
- **Clickable status** opens detailed status view
- **Double-click** opens embedded web chat
- **Open Dashboard** opens browser with authenticated session

### Session Awareness
- **Live session tracking** — see main and sub-sessions in real-time
- **Session detail** — model, channel, current activity per session
- **Activity display** — "Main · 💻 pnpm test" or "Sub · 📄 reading file"

### Usage & Context
- **Token usage** display (input/output/total with human-readable formatting)
- **Cost tracking** when available from gateway
- **Request count** and active model

### WebChat Panel
- **Embedded chat** via WebView2 — no browser needed
- **Dark mode** background
- Toolbar with home, refresh, pop-out to browser, and DevTools
- Singleton window (double-click tray icon or "Open Web Chat" menu)

### Notifications
- **Windows toast notifications** with per-type filtering:
  - 🩸 Health / CGM alerts
  - 🚨 Urgent / error alerts  
  - ⏰ Reminders
  - 📧 Email notifications
  - 📅 Calendar events
  - 🔨 Build / CI
  - 📦 Stock availability
  - 🤖 General info
- **Clickable toasts** — Quick Send toasts open dashboard when clicked
- **Notification history** — scrollable list with timestamps, even for filtered-out notifications
- Fallback to balloon tips if toast fails

### Channel Health
- Live WhatsApp, Telegram, and other channel status
- Smart status detection: `[READY]` (probe OK), `[LINKED]` (authenticated), `[ON]`/`[OFF]`
- Shows linked state, auth age, errors, and stale warnings
- On-demand health check button

### Keyboard Shortcuts
- **Ctrl+Alt+Shift+C** — Global hotkey to open Quick Send from anywhere
- **Enter** — Send message in Quick Send dialog
- **Shift+Enter** — New line in Quick Send
- **Esc** — Cancel Quick Send

### Deep Links
- Registers `Moltbot://` URI scheme
- `Moltbot://agent?message=Hello` sends a message to the agent
- Confirmation prompt for safety (bypass with `key` parameter)

### Quality of Life
- **ARM64 support** — native builds for Windows on ARM
- **Auto-start** via Windows Registry
- **Exponential backoff** on reconnect (1s → 60s)
- **File logging** to `%LOCALAPPDATA%\MoltbotTray\moltbot-tray.log` (with rotation at 1MB)
- **Open Log File** menu item for quick debugging
- **Single instance** enforcement (mutex)
- **Proper GDI handle cleanup** (no icon leaks)
- **Status detail view** — rich dark-themed status panel

## Requirements

- Windows 10 version 1903+ (for toast notifications)
- .NET 10 Runtime (included in self-contained builds)
- [WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) (for chat panel)
- Moltbot gateway running (typically in WSL2)

## Quick Start

1. Download the latest release from [Releases](https://github.com/shanselman/moltbot-windows-hub/releases)
   - **x64**: For Intel/AMD processors
   - **arm64**: For Windows on ARM (e.g., Surface Pro X, Snapdragon laptops)
2. Run `MoltbotTray.exe`
3. Right-click tray icon → Settings
4. Enter gateway URL (`ws://localhost:18789`) and your token
5. Done — you'll see the icon turn green when connected

### Finding Your Gateway Token

```bash
# In WSL2:
cat ~/.Moltbot/Moltbot.json | grep token
# Or:
Moltbot config get gateway.auth.token
```

## Build from Source

```bash
git clone https://github.com/shanselman/moltbot-windows-hub.git
cd moltbot-windows-hub

# Windows — auto-detects architecture
build.bat

# Manual build
dotnet build -c Release -r win-x64
dotnet build -c Release -r win-arm64

# Self-contained single-file executable
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
dotnet publish -c Release -r win-arm64 --self-contained -p:PublishSingleFile=true -o publish-arm64

# Cross-compile from Linux (for CI)
dotnet build -p:EnableWindowsTargeting=true -r win-x64
```

## Project Structure

```
├── Program.cs                    # Entry point, single instance, deep link registration
├── TrayApplication.cs            # Tray icon, menu, event wiring, UI updates
├── MoltbotGatewayClient.cs      # WebSocket client, protocol v3, event parsing, state tracking
├── WebChatForm.cs                # WebView2 chat panel (singleton, dark mode)
├── QuickSendDialog.cs            # Quick message input (Enter to send, TopMost)
├── StatusDetailForm.cs           # Rich status detail view (dark theme)
├── NotificationHistoryForm.cs    # Scrollable notification history
├── GlobalHotkey.cs               # Ctrl+Alt+Shift+C system-wide hotkey
├── DeepLinkHandler.cs            # Moltbot:// URI scheme handler
├── SettingsManager.cs            # JSON config with notification filters
├── SettingsDialog.cs             # Settings UI (connection, startup, notification filters)
├── AutoStartManager.cs           # Windows Registry auto-start
├── Logger.cs                     # File + debug logger with rotation
└── MoltbotTray.csproj           # .NET 10, Windows Forms, WebView2
```

## macOS Parity Status

This Windows tray app aims for feature parity with the [Moltbot macOS menu bar app](https://github.com/Moltbot/Moltbot-macos).

| Feature | macOS | Windows | Notes |
|---------|:-----:|:-------:|-------|
| System tray/menu bar icon | ✅ | ✅ | Lobster 🦞 when connected |
| Status colors/indicators | ✅ | ✅ | Text labels `[ON]/[OFF]` for clarity |
| Activity badges | ✅ | ✅ | exec/read/write/search/browser |
| Toast/native notifications | ✅ | ✅ | Windows toast + fallback |
| Per-type notification filters | ✅ | ✅ | Health, urgent, email, etc. |
| Clickable notifications | ✅ | ✅ | Opens dashboard with auth |
| Notification history | — | ✅ | Windows-only feature |
| Embedded chat (WebView) | ✅ | ✅ | WebView2 |
| Open Dashboard in browser | ✅ | ✅ | Token auto-included |
| Channel health display | ✅ | ✅ | Telegram, WhatsApp status |
| Session awareness (main/sub) | ✅ | ✅ | Live session tracking |
| Usage/token display | ✅ | ✅ | Input/output/total |
| Deep link URI scheme | ✅ | ✅ | `Moltbot://` |
| Global hotkey | — | ✅ | Ctrl+Alt+Shift+C |
| Auto-start | ✅ | ✅ | Registry-based |
| Quick send | ✅ | ✅ | Fire-and-forget to main session |
| Health check (on-demand) | ✅ | ✅ | |
| Status detail view | — | ✅ | Windows-only feature |
| File logging | ✅ | ✅ | With rotation |
| ARM64 support | ✅ | ✅ | Apple Silicon / Windows ARM |
| Canvas panel | ✅ | 🔜 | Planned |
| Voice wake / push-to-talk | ✅ | 🔜 | Planned |
| Skills settings UI | ✅ | 🔜 | Planned |
| TCC permissions management | ✅ | N/A | macOS-specific |
| PeekabooBridge (UI automation) | ✅ | N/A | macOS-specific |
| XPC / node host service | ✅ | N/A | macOS-specific |

## Settings

Settings are stored in `%APPDATA%\MoltbotTray\settings.json`:

```json
{
  "GatewayUrl": "ws://localhost:18789",
  "Token": "your-token",
  "AutoStart": false,
  "ShowNotifications": true,
  "NotificationSound": "Default",
  "NotifyHealth": true,
  "NotifyUrgent": true,
  "NotifyReminder": true,
  "NotifyEmail": true,
  "NotifyCalendar": true,
  "NotifyBuild": true,
  "NotifyStock": true,
  "NotifyInfo": true,
  "ShowGlobalHotkey": true
}
```

## Troubleshooting

**Can't connect?**
- Check gateway: `Moltbot gateway status` in WSL2
- Verify token matches `~/.Moltbot/Moltbot.json`
- Try WSL2 IP directly: `ws://<wsl-ip>:18789` (`wsl hostname -I`)

**No notifications?**
- Check Windows Settings → Notifications
- Check Focus Assist / Do Not Disturb
- Check notification filter settings in the app

**WebChat blank?**
- Install [WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/)
- Check logs: `%LOCALAPPDATA%\MoltbotTray\moltbot-tray.log`
- Right-click tray → Open Log File

**Global hotkey not working?**
- Another app may have registered Ctrl+Alt+Shift+C
- Check Settings → Global hotkey is enabled
- Check the log file for "Failed to register global hotkey"

## License

MIT

## Credits

- Built with .NET 10, Windows Forms, and [WebView2](https://developer.microsoft.com/en-us/microsoft-edge/webview2/)
- Toast notifications via [Microsoft.Toolkit.Uwp.Notifications](https://github.com/CommunityToolkit/WindowsCommunityToolkit)
- Part of the [Moltbot](https://github.com/Moltbot/Moltbot) ecosystem

