# Notification Categorization

The tray app categorizes incoming notifications to apply per-category filters, display appropriate icons, and let users control which notifications they see.

## How It Works

Notifications flow through a **layered pipeline** — the first layer that matches wins:

```
Structured Metadata  →  User Rules  →  Keyword Matching  →  Default (info)
```

### 1. Structured Metadata (highest priority)

If the gateway sends metadata on the notification, it is used directly:

- **Intent** (e.g. `reminder`, `build`, `alert`) — maps to a category
- **Channel** (e.g. `email`, `calendar`, `ci`) — maps to a category

This eliminates misclassification. A chat response that mentions "email" won't be categorized as email — the gateway knows the actual source.

> **Note:** The gateway does not send structured metadata yet. When it does, categorization will automatically improve with no client changes needed.

### 2. User-Defined Rules

Custom regex or keyword rules, evaluated in order. Configure these in `%APPDATA%\OpenClawTray\settings.json`:

```json
{
  "UserRules": [
    {
      "Pattern": "invoice|receipt",
      "IsRegex": true,
      "Category": "email",
      "Enabled": true
    },
    {
      "Pattern": "deploy to prod",
      "IsRegex": false,
      "Category": "urgent",
      "Enabled": true
    }
  ]
}
```

Rules match against both the notification title and message (case-insensitive). Invalid regex patterns are silently skipped.

### 3. Keyword Matching (legacy fallback)

The original keyword-based system, preserved for backward compatibility:

| Category | Keywords | Icon |
|----------|----------|------|
| `health` | blood sugar, glucose, cgm, mg/dl | 🩸 |
| `urgent` | urgent, critical, emergency | 🚨 |
| `reminder` | reminder | ⏰ |
| `stock` | stock, in stock, available now | 📦 |
| `email` | email, inbox, gmail | 📧 |
| `calendar` | calendar, meeting, event | 📅 |
| `error` | error, failed, exception | ⚠️ |
| `build` | build, ci, deploy | 🔨 |
| `info` | *(everything else)* | 🤖 |

### 4. Default

If nothing matches, the notification is categorized as `info`.

## Chat Response Toggle

Notifications are either **chat responses** (replies from an AI agent) or **system notifications** (alerts, reminders, build status, etc.). The `NotifyChatResponses` setting controls whether chat responses generate Windows toasts:

| Setting | Chat Responses | System Notifications |
|---------|----------------|----------------------|
| `true` (default) | ✅ Shown | ✅ Shown |
| `false` | ❌ Suppressed | ✅ Shown |

This is useful when you're having a conversation through another device and don't want every reply popping up as a toast on your desktop.

## Settings

All notification settings are in `%APPDATA%\OpenClawTray\settings.json`:

```json
{
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

  "NotifyChatResponses": true,
  "PreferStructuredCategories": true,
  "UserRules": []
}
```

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `ShowNotifications` | bool | `true` | Master toggle for all notifications |
| `NotifyHealth` | bool | `true` | Show health/glucose alerts |
| `NotifyUrgent` | bool | `true` | Show urgent alerts (also covers `error` type) |
| `NotifyReminder` | bool | `true` | Show reminders |
| `NotifyEmail` | bool | `true` | Show email notifications |
| `NotifyCalendar` | bool | `true` | Show calendar events |
| `NotifyBuild` | bool | `true` | Show build/CI/deploy notifications |
| `NotifyStock` | bool | `true` | Show stock alerts |
| `NotifyInfo` | bool | `true` | Show general info notifications |
| `NotifyChatResponses` | bool | `true` | Show chat response toasts |
| `PreferStructuredCategories` | bool | `true` | Use gateway metadata over keywords |
| `UserRules` | array | `[]` | Custom categorization rules (see above) |

## Channel and Agent Mapping

When structured metadata is available, channels and agents map to categories:

**Channel → Category:**
| Channel | Category |
|---------|----------|
| `calendar` | calendar |
| `email` | email |
| `ci`, `build` | build |
| `stock`, `inventory` | stock |
| `health` | health |
| `alerts` | urgent |

**Agent mapping** is also supported — per-agent category defaults can be added to the channel map in `NotificationCategorizer.cs`.

## Architecture

The categorization logic lives in `OpenClaw.Shared.NotificationCategorizer`, making it available to both the WinUI tray app and any other consumers of the shared library. The gateway client (`OpenClawGatewayClient`) calls the categorizer when emitting notifications, and the tray app's `ShouldShowNotification` method applies the per-category and chat-toggle filters before showing a toast.
