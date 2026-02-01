# 🦞 OpenClaw Windows Hub

A Windows companion suite for [OpenClaw](https://openclaw.ai) - the AI-powered personal assistant.

*Made with 🦞 love by Scott Hanselman and Molty*

![Molty - Windows Tray App](docs/molty1.png)

![Molty - Command Palette](docs/molty2.png)

## Projects

This monorepo contains three projects:

| Project | Description |
|---------|-------------|
| **OpenClaw.Tray** | System tray application for quick access to OpenClaw |
| **OpenClaw.Shared** | Shared gateway client library |
| **OpenClaw.CommandPalette** | PowerToys Command Palette extension |

## 🚀 Quick Start

### Prerequisites
- Windows 10/11
- .NET 10.0 SDK (preview) - https://dotnet.microsoft.com/download/dotnet/10.0
- PowerToys (optional, for Command Palette extension)

### Build

Use the build script to check prerequisites and build:

```powershell
# Check prerequisites
.\build.ps1 -CheckOnly

# Build all projects
.\build.ps1

# Build specific project
.\build.ps1 -Project WinUI
.\build.ps1 -Project Tray -Configuration Release
```

Or build directly with dotnet:

```powershell
# Build all
dotnet build

# Build specific project (note: full path to .csproj required)
dotnet build src/OpenClaw.Tray/OpenClaw.Tray.csproj
dotnet build src/OpenClaw.Tray.WinUI/OpenClaw.Tray.WinUI.csproj
```

### Run Tray App

```powershell
# WinForms version
dotnet run --project src/OpenClaw.Tray/OpenClaw.Tray.csproj

# WinUI version - run the exe directly
.\src\OpenClaw.Tray.WinUI\bin\Debug\net10.0-windows10.0.19041.0\OpenClaw.Tray.WinUI.exe
```

## 📦 OpenClaw.Tray (Molty)

Modern Windows 11-style system tray companion that connects to your local OpenClaw gateway.

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

Comparing against [openclaw-menubar](https://github.com/magimetal/openclaw-menubar) (macOS Swift menu bar app):

| Feature | Mac | Windows | Notes |
|---------|-----|---------|-------|
| Menu bar/tray icon | ✅ | ✅ | Color-coded status |
| Gateway status display | ✅ | ✅ | Connected/Disconnected |
| PID display | ✅ | ❌ | Mac shows gateway PID |
| Channel status | ✅ | ✅ | Mac: Discord / Win: Telegram+WhatsApp |
| Sessions count | ✅ | ✅ | |
| Last check timestamp | ✅ | ✅ | Shown in tray tooltip |
| Gateway start/stop/restart | ✅ | ❌ | Mac controls gateway process |
| View Logs | ✅ | ✅ | |
| Open Web UI | ✅ | ✅ | |
| Refresh | ✅ | ✅ | Auto-refresh on menu open |
| Launch at Login | ✅ | ✅ | |
| Notifications toggle | ✅ | ✅ | |

### Windows-Only Features

These features are available in Windows but not in the Mac app:

| Feature | Description |
|---------|-------------|
| Quick Send hotkey | Ctrl+Alt+Shift+C global hotkey |
| Embedded Web Chat | WebView2-based chat window |
| Toast notifications | Clickable Windows notifications |
| Channel control | Start/stop Telegram & WhatsApp |
| Modern flyout menu | Windows 11-style with dark/light mode |
| Deep links | `openclaw://` URL scheme with IPC |
| First-run welcome | Guided onboarding for new users |
| PowerToys integration | Command Palette extension |

### Deep Links

OpenClaw registers the `openclaw://` URL scheme for automation and integration:

| Link | Description |
|------|-------------|
| `openclaw://settings` | Open Settings dialog |
| `openclaw://chat` | Open Web Chat window |
| `openclaw://dashboard` | Open Dashboard in browser |
| `openclaw://dashboard/sessions` | Open specific dashboard page |
| `openclaw://send?message=Hello` | Open Quick Send with pre-filled text |
| `openclaw://agent?message=Hello` | Send message directly (with confirmation) |

Deep links work even when Molty is already running - they're forwarded via IPC.

## 📦 OpenClaw.CommandPalette

PowerToys Command Palette extension for quick OpenClaw access.

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
4. Type "OpenClaw" to see commands

## 📦 OpenClaw.Shared

Shared library containing:
- `OpenClawGatewayClient` - WebSocket client for gateway protocol
- `IOpenClawLogger` - Logging interface
- Data models (SessionInfo, ChannelHealth, etc.)
- Channel control (start/stop channels via gateway)

## Development

### Project Structure
```
moltbot-windows-hub/
├── src/
│   ├── OpenClaw.Shared/           # Shared gateway library
│   ├── OpenClaw.Tray/             # System tray app
│   └── OpenClaw.CommandPalette/   # PowerToys extension
├── docs/
│   └── molty1.png                # Screenshot
├── moltbot-windows-hub.sln
├── README.md
├── LICENSE
└── .gitignore
```

### Configuration

Settings are stored in:
- Settings: `%APPDATA%\OpenClawTray\settings.json`
- Logs: `%LOCALAPPDATA%\OpenClawTray\openclaw-tray.log`

Default gateway: `ws://localhost:18789`

### First Run

On first run without a token, Molty displays a welcome dialog that:
1. Explains what's needed to get started
2. Links to [documentation](https://docs.molt.bot/web/dashboard) for token setup
3. Opens Settings to configure the connection

## License

MIT License - see [LICENSE](LICENSE)

---

*Formerly known as Moltbot, formerly known as Clawdbot*

