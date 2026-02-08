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
- Windows 10 (20H2+) or Windows 11
- .NET 10.0 SDK (preview) - https://dotnet.microsoft.com/download/dotnet/10.0
- Windows 10 SDK (for WinUI build) - install via Visual Studio or standalone
- WebView2 Runtime - pre-installed on modern Windows, or get from https://developer.microsoft.com/microsoft-edge/webview2
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
# Build all (use build.ps1 for best results)
dotnet build

# Build WinUI (requires runtime identifier for WebView2 support)
dotnet build src/OpenClaw.Tray.WinUI/OpenClaw.Tray.WinUI.csproj -r win-arm64  # ARM64
dotnet build src/OpenClaw.Tray.WinUI/OpenClaw.Tray.WinUI.csproj -r win-x64    # x64

# Build MSIX package (for camera/mic consent prompts)
dotnet build src/OpenClaw.Tray.WinUI -r win-arm64 -p:PackageMsix=true  # ARM64 MSIX
dotnet build src/OpenClaw.Tray.WinUI -r win-x64 -p:PackageMsix=true    # x64 MSIX
```

### Run Tray App

```powershell
# WinForms version
dotnet run --project src/OpenClaw.Tray/OpenClaw.Tray.csproj

# WinUI version - run the exe directly (path includes runtime identifier)
.\src\OpenClaw.Tray.WinUI\bin\Debug\net10.0-windows10.0.19041.0\win-arm64\OpenClaw.Tray.WinUI.exe  # ARM64
.\src\OpenClaw.Tray.WinUI\bin\Debug\net10.0-windows10.0.19041.0\win-x64\OpenClaw.Tray.WinUI.exe    # x64
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
- 🔔 **Toast Notifications** - Clickable Windows notifications with [smart categorization](docs/NOTIFICATION_CATEGORIZATION.md)
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

### 🔌 Node Mode (Agent Control)

When Node Mode is enabled in Settings, your Windows PC becomes a **node** that the OpenClaw agent can control - just like the Mac app! The agent can:

| Capability | Commands | Description |
|------------|----------|-------------|
| **System** | `system.notify`, `system.run`, `system.execApprovals.get`, `system.execApprovals.set` | Show Windows toast notifications, execute commands with policy controls |
| **Canvas** | `canvas.present`, `canvas.hide`, `canvas.navigate`, `canvas.eval`, `canvas.snapshot`, `canvas.a2ui.push` (investigating), `canvas.a2ui.reset` (investigating) | Display and control a WebView2 window |
| **Screen** | `screen.capture`, `screen.list` | Capture screenshots |
| **Camera** | `camera.list`, `camera.snap` | Enumerate cameras and capture a still photo |

#### Node Setup

1. **Enable Node Mode** in Settings (enabled by default)
2. **First connection** creates a pairing request on the gateway
3. **Approve the device** on your gateway:
   ```bash
   openclaw devices list          # Find your Windows device
   openclaw devices approve <id>  # Approve it
   ```
4. **Configure gateway allowCommands** - Add the commands you want to allow in `~/.openclaw/openclaw.json`:
   ```json
   {
     "nodes": {
       "allowCommands": [
         "system.notify",
         "system.run",
          "system.execApprovals.get",
          "system.execApprovals.set",
         "canvas.present",
         "canvas.hide",
         "canvas.navigate",
         "canvas.eval",
         "canvas.snapshot",
         "canvas.a2ui.push",
         "canvas.a2ui.reset",
         "screen.capture",
         "screen.list",
         "camera.list",
         "camera.snap"
       ]
     }
   }
    ```
   > ⚠️ **Important**: The gateway has a server-side allowlist. Commands must be listed explicitly - wildcards like `canvas.*` don't work!

5. **Test it** from your Mac/gateway:
   ```bash
    # Show a notification
    openclaw nodes notify --node <id> --title "Hello" --body "From Mac!"
    
    # Open a canvas window
    openclaw nodes canvas present --node <id> --url "https://example.com"
    
    # Execute JavaScript (note: CLI sends "javaScript" param)
    openclaw nodes canvas eval --node <id> --javaScript "document.title"
    
    # Render A2UI JSONL in the canvas (pass the file contents as a string)
    openclaw nodes canvas a2ui push --node <id> --jsonl "$(Get-Content -Raw .\\ui.jsonl)"
    
    # Take a screenshot
    openclaw nodes invoke --node <id> --command screen.capture --params '{"screenIndex":0,"format":"png"}'

    # List cameras
    openclaw nodes invoke --node <id> --command camera.list

    # Take a photo (NV12/MediaCapture fallback)
    openclaw nodes invoke --node <id> --command camera.snap --params '{"deviceId":"<device-id>","format":"jpeg","quality":80}'

    # Execute a command on the Windows node
    openclaw nodes invoke --node <id> --command system.run --params '{"command":"Get-Process | Select -First 5","shell":"powershell","timeout":10000}'

    # View exec approval policy
    openclaw nodes invoke --node <id> --command system.execApprovals.get

    # Update exec approval policy (add custom rules)
    openclaw nodes invoke --node <id> --command system.execApprovals.set --params '{"rules":[{"pattern":"echo *","action":"allow"},{"pattern":"*","action":"deny"}],"defaultAction":"deny"}'
    ```
    > 📷 **Camera permission**: Desktop builds rely on Windows Privacy settings. Packaged MSIX builds will show the system consent prompt.
    
    > 🔒 **Exec Policy**: `system.run` is gated by an approval policy (saved to `exec-policy.json`). Default rules allow read-only commands (echo, Get-*, hostname, etc.) and deny destructive operations (rm, shutdown, registry edits). Use `system.execApprovals.get/set` to view/modify rules remotely.

#### Node Status in Tray Menu

The tray menu shows node connection status:
- **🔌 Node Mode** section appears when enabled
- **⏳ Waiting for approval...** - Device needs approval on gateway
- **✅ Paired & Connected** - Ready to receive commands
- Click the device ID to copy it for the approval command

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

