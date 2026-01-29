# 🦞 Moltbot Windows Hub

A Windows companion suite for [Moltbot](https://moltbot.com) - the AI-powered personal assistant.

![Molty - Windows Tray App](docs/molty1.png)

## Projects

This monorepo contains three projects:

| Project | Description |
|---------|-------------|
| **Moltbot.Tray** | System tray application for quick access to Moltbot |
| **Moltbot.Shared** | Shared gateway client library |
| **Moltbot.CommandPalette** | PowerToys Command Palette extension |

## 🚀 Quick Start

### Prerequisites
- .NET 10.0 SDK
- Windows 10/11
- PowerToys (for Command Palette extension)

### Build
```bash
dotnet build
```

### Run Tray App
```bash
dotnet run --project src/Moltbot.Tray
```

## 📦 Moltbot.Tray (Molty)

Modern Windows 11-style system tray companion that connects to your local Moltbot gateway.

### Features
- 🦞 **Lobster branding** - Pixel-art lobster tray icon with status colors
- 🎨 **Modern UI** - Windows 11 flyout menu with dark/light mode support
- 💬 **Quick Send** - Send messages via global hotkey (Ctrl+Alt+Shift+C)
- 🔄 **Auto-updates** - Automatic updates from GitHub Releases
- 🌐 **Web Chat** - Embedded chat window with WebView2
- 📊 **Live Status** - Real-time sessions, channels, and usage display
- 🔔 **Toast Notifications** - Clickable Windows notifications with filters
- 📡 **Channel Control** - Start/stop Telegram & WhatsApp from the menu
- ⏱ **Cron Jobs** - Quick access to scheduled tasks
- 🚀 **Auto-start** - Launch with Windows
- ⚙️ **Settings** - Full configuration dialog
- 🎯 **First-run experience** - Welcome dialog guides new users

### Menu Sections
- **Status** - Gateway connection status with click-to-view details
- **Sessions** - Active agent sessions (clickable → dashboard)
- **Channels** - Telegram/WhatsApp status with toggle control
- **Actions** - Dashboard, Web Chat, Quick Send, Cron Jobs, History
- **Settings** - Configuration, auto-start, logs

### Mac Parity Status

| Feature | Mac | Windows |
|---------|-----|---------|
| System tray icon | ✅ | ✅ |
| Connection status | ✅ | ✅ |
| Quick send hotkey | ✅ | ✅ |
| Web chat window | ✅ | ✅ |
| Toast notifications | ✅ | ✅ |
| Auto-start | ✅ | ✅ |
| Session display | ✅ | ✅ |
| Channel health | ✅ | ✅ |
| Channel control | ✅ | ✅ |
| Modern UI styling | ✅ | ✅ |
| Dark/Light mode | ✅ | ✅ |
| Deep links | ✅ | ✅ |

### Deep Links

Moltbot registers the `moltbot://` URL scheme for automation and integration:

| Link | Description |
|------|-------------|
| `moltbot://settings` | Open Settings dialog |
| `moltbot://chat` | Open Web Chat window |
| `moltbot://dashboard` | Open Dashboard in browser |
| `moltbot://dashboard/sessions` | Open specific dashboard page |
| `moltbot://send?message=Hello` | Open Quick Send with pre-filled text |
| `moltbot://agent?message=Hello` | Send message directly (with confirmation) |

Deep links work even when Molty is already running - they're forwarded via IPC.

## 📦 Moltbot.CommandPalette

PowerToys Command Palette extension for quick Moltbot access.

### Commands
- **🦞 Open Dashboard** - Launch web dashboard
- **💬 Quick Send** - Send a message
- **📊 Full Status** - View gateway status
- **⚡ Sessions** - View active sessions
- **📡 Channels** - View channel health
- **🔄 Health Check** - Trigger health refresh

### Installation
1. Build the solution in Release mode
2. Deploy the MSIX package via Visual Studio
3. Open Command Palette (Win+Alt+Space)
4. Type "Moltbot" to see commands

## 📦 Moltbot.Shared

Shared library containing:
- `MoltbotGatewayClient` - WebSocket client for gateway protocol
- `IMoltbotLogger` - Logging interface
- Data models (SessionInfo, ChannelHealth, etc.)
- Channel control (start/stop channels via gateway)

## Development

### Project Structure
```
moltbot-windows-hub/
├── src/
│   ├── Moltbot.Shared/           # Shared gateway library
│   ├── Moltbot.Tray/             # System tray app
│   └── Moltbot.CommandPalette/   # PowerToys extension
├── docs/
│   └── molty1.png                # Screenshot
├── moltbot-windows-hub.sln
├── README.md
├── LICENSE
└── .gitignore
```

### Configuration

Settings are stored in:
- Settings: `%APPDATA%\MoltbotTray\settings.json`
- Logs: `%LOCALAPPDATA%\MoltbotTray\moltbot-tray.log`

Default gateway: `ws://localhost:18789`

### First Run

On first run without a token, Molty displays a welcome dialog that:
1. Explains what's needed to get started
2. Links to [documentation](https://docs.molt.bot/web/dashboard) for token setup
3. Opens Settings to configure the connection

## License

MIT License - see [LICENSE](LICENSE)
