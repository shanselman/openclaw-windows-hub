# Development Notes

## Architecture Overview

This Windows system tray application is built with .NET 10 and Windows Forms, designed to be lightweight and efficient while providing seamless integration with the Moltbot gateway. It mirrors the macOS menu bar app's functionality for Windows users.

### Component Architecture

```
┌──────────────┐     ┌─────────────────────────┐
│   Program    │────▶│    TrayApplication       │
│  (entry)     │     │  - System tray icon      │
│  - Mutex     │     │  - Context menu          │
│  - URI reg   │     │  - Event dispatch (UI)   │
└──────────────┘     │  - Session awareness     │
                     └────────┬────────────────┘
                              │ events
                     ┌────────▼────────────────┐
                     │  MoltbotGatewayClient   │
                     │  - WebSocket connection  │
                     │  - Protocol v3 handshake │
                     │  - Event parsing         │
                     │  - Session/usage tracking│
                     │  - Auto-reconnect        │
                     └─────────────────────────┘
```

### Key Components

| Component | File | Purpose |
|-----------|------|---------|
| **Program** | `Program.cs` | Entry point, single-instance mutex, URI scheme registration |
| **TrayApplication** | `TrayApplication.cs` | Main `ApplicationContext` managing the tray icon, context menu, and UI event dispatch |
| **MoltbotGatewayClient** | `MoltbotGatewayClient.cs` | WebSocket client implementing gateway protocol v3 with event parsing, session tracking, and usage monitoring |
| **SettingsManager** | `SettingsManager.cs` | JSON-based settings persistence in `%APPDATA%\MoltbotTray\` |
| **SettingsDialog** | `SettingsDialog.cs` | Settings UI with URL/token config, test connection (with timeout), and notification preferences |
| **Logger** | `Logger.cs` | Thread-safe file + debug logger with automatic rotation (1MB), writes to `%LOCALAPPDATA%\MoltbotTray\moltbot-tray.log` |
| **DeepLinkHandler** | `DeepLinkHandler.cs` | `Moltbot://` URI scheme registration and processing for cross-app integration |
| **WebChatForm** | `WebChatForm.cs` | WebView2-based chat panel (singleton) with toolbar, fallback to browser |
| **QuickSendDialog** | `QuickSendDialog.cs` | Lightweight dialog for sending messages (supports Ctrl+Enter) |
| **StatusDetailForm** | `StatusDetailForm.cs` | Rich status view showing gateway connection, sessions, channels, usage, and app info |
| **NotificationHistoryForm** | `NotificationHistoryForm.cs` | Scrollable history of received notifications |
| **AutoStartManager** | `AutoStartManager.cs` | Windows startup integration via `HKCU\...\Run` registry key |
| **GlobalHotkey** | `GlobalHotkey.cs` | System-wide hotkey registration for quick access |

### Data Flow

1. **Gateway → Client**: WebSocket messages parsed into typed events (`agent`, `chat`, `health`, `session`, `usage`)
2. **Client → TrayApp**: C# events marshaled to UI thread via `SynchronizationContext.Post`
3. **TrayApp → UI**: Context menu items, tray icon, and toast notifications updated

### Event Types from Gateway

| Event | Handler | UI Result |
|-------|---------|-----------|
| `agent` (stream=job) | `HandleJobEvent` | Activity row update, icon badge |
| `agent` (stream=tool) | `HandleToolEvent` | Activity row with tool name + args detail |
| `chat` | `HandleChatEvent` | Toast notification for short assistant messages |
| `health` | `ParseChannelHealth` | Channel health rows in context menu |
| `session` | `HandleSessionEvent` | Session list refresh |
| `usage` | `ParseUsage` | Usage row (tokens, model, cost) |

### Notification Classification

Notifications are classified two ways:

1. **Structured** (preferred): Events with explicit `type`, `category`, or `notificationType` fields
2. **Text-based** (fallback): Keyword matching on notification content (glucose, reminder, stock, email, calendar, etc.)

### Session Awareness

The activity display uses a stable session selection algorithm:

1. Active main session always takes priority
2. Currently displayed session is kept if still active (prevents flip-flop)
3. Falls back to most recently active sub-session
4. 3-second debounce window prevents jitter during rapid activity changes

### Tray Icon

The tray icon is a programmatically drawn 16×16 circle:

- **Teal**: Connected
- **Amber**: Connecting
- **Red**: Error
- **Gray**: Disconnected

An activity badge (small corner dot) appears during tool execution:
- **Orange**: exec (running commands)
- **Green**: write/edit (file changes)
- **Blue**: read (file access)
- **Purple**: search/browser (web activity)

### Settings Storage

Settings are stored as JSON in `%APPDATA%\MoltbotTray\settings.json`:

```json
{
  "GatewayUrl": "ws://localhost:18789",
  "Token": "...",
  "AutoStart": false,
  "ShowNotifications": true,
  "NotificationSound": "Default"
}
```

### Deep Links

The app registers `Moltbot://` URI scheme for cross-app integration:

```
Moltbot://agent?message=Hello&key=optional-auth-key
```

Without a key, the user is prompted before sending. With a key, the message is sent directly.

## Build & Development

### Prerequisites

- .NET 10 SDK
- Windows 10 SDK (19041+) — cross-compilation from Linux supported via `EnableWindowsTargeting`
- WebView2 Runtime (for chat panel, optional at runtime)

### Build

```bash
dotnet build
```

### Publish (single-file, self-contained)

```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

### CI

GitHub Actions builds on every push. Check status:

```bash
gh run list --repo shanselman/moltbot-windows-hub --limit 1
```

## Dependencies

| Package | Purpose |
|---------|---------|
| `Microsoft.Toolkit.Uwp.Notifications` (7.1.3) | Toast notifications with rich content |
| `Microsoft.Web.WebView2` (1.0.3124.44) | Embedded browser for chat panel |
| `System.Text.Json` (9.0.0) | JSON serialization for settings and gateway protocol |

## Security Considerations

- **Token storage**: Plaintext in user AppData (future: Windows Credential Manager)
- **Deep links**: Untrusted deep links prompt user confirmation
- **WebSocket**: Supports both `ws://` (local) and `wss://` (remote)
- **Auto-start**: Registry-based, current user only (no elevation needed)
- **Logging**: Sensitive data (tokens) not logged

## Known Limitations

- Toast notifications require Windows 10 1903+
- WebView2 Runtime must be installed separately for chat panel
- Single-instance enforced via Mutex (deep link forwarding to running instance TODO)
- Tray icon tooltip limited to 63 characters (full detail shown in context menu activity row)

