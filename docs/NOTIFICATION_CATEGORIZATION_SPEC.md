# Enhanced Notification Categorization Spec

## Summary
Replace the current keyword-only categorization with a layered system that prefers structured metadata (channel/session/agent/context), supports user-defined rules, and provides a simple toggle for chat responses vs. system notifications.

## Goals
- Prevent chat responses from being misclassified as system notifications.
- Allow per-channel/session and per-agent routing without breaking existing settings.
- Let users define custom rules (regex/keyword).
- Use gateway intent/context when available.
- Provide an easy way to mute all chat-response notifications.

## Non-Goals
- Redesigning the entire settings UI.
- Changing notification delivery mechanisms (toast history, sounds, etc.).
- Deprecating existing keyword categories immediately.

## Current Behavior
- `ClassifyNotification(string text) -> (string title, string type)` matches fixed keywords and returns category/title.
- Settings UI documents keyword-based filtering.
- Per-category toggles (Health/Urgent/Reminder/etc.) are used to filter display.

## Proposed Data Model

### Notification Metadata (from gateway)
Extend the gateway notification payload with optional metadata and map it into `OpenClawNotification` in `OpenClawGatewayClient` (`OpenClaw.Shared/OpenClawGatewayClient.cs`):
- `Channel` (string): e.g., `telegram`, `whatsapp`, `email`, `calendar`, `chat`. When `SessionKey` is present and `Channel` is missing, `OpenClawGatewayClient` should copy the gateway session `channel` field into this metadata value during payload mapping.
- `SessionKey` (string): gateway session id.
- `Agent` (string): agent name/identifier.
- `Intent` (string): normalized intent (e.g., `reminder`, `build`, `alert`).
- `IsChat` (bool): explicit flag for chat responses (already exists in `OpenClawNotification`).
- `Tags` (string[]): free-form tags for routing.

### Settings (persisted)
Add a new notification section in settings:
- `NotifyChatResponses` (bool): master toggle for chat responses.
- `ChannelRules` (list): per-channel enable/disable (default: true).
- `AgentRules` (list): per-agent enable/disable (default: true).
- `UserRules` (list): regex or keyword rules (category + enable/disable).
- `PreferStructuredCategories` (bool): default true.

## Categorization Pipeline
Order of operations (first match wins):
1. **Chat toggle**: If `IsChat` is true and `NotifyChatResponses` is false, then suppress.
2. **Structured category**:
   - If `Intent` is provided → map to category table.
   - If `Channel` or `Agent` maps to a fixed category → use it.
   - If `Agent` is missing, optionally skip agent-based rules and rely on `Channel` mapping only (open question below).
3. **User rules**:
   - Regex/keyword rules over `Title` + `Message`.
4. **Legacy keyword fallback**:
   - Use existing keyword matching for backward compatibility.
5. **Default**:
   - `info`.

## Channel/Agent Categorization

### Channel mapping (examples)
- `calendar` → `calendar`
- `email` → `email`
- `ci`/`build` → `build`
- `inventory`/`stock` → `stock`
- `chat`/`assistant` → `info` (only when `IsChat` is false or chat responses are enabled)
- `health` → `health`
- `alerts` → `urgent`

### Agent mapping
Allow optional per-agent category defaults (e.g., `infra-bot` → `build`).

## User-Defined Rules
Rules evaluated in order:
- `Match`: regex/keyword
- `Category`: `health|urgent|reminder|email|calendar|build|stock|error|info`
- `Enabled`: true/false
Storage example:
```json
{
  "pattern": "invoice|receipt",
  "isRegex": true,
  "category": "email",
  "enabled": true
}
```

## UI Changes (Minimal)
Settings → Notifications:
- Add toggle: **“Show chat responses”**
- Add collapsible advanced section:
  - Per-channel toggles (if available).
  - Per-agent toggles (if available).
  - Rule list with add/remove (regex/keyword).
  - Note: “Structured metadata overrides keyword matching.”

## Migration Strategy
- Default new toggles to `true` to preserve existing behavior.
- If no structured metadata arrives, system behaves exactly as today.
- Preserve keyword-based explanation text but add note about structured overrides.

## Telemetry/Debugging (Optional)
Add a debug log line on classification:
`[INFO] Notification categorized as {category} (source=structured|user-rule|keyword|default)`

## Testing Plan
- Unit tests for classification order (structured > user rules > keywords).
- Unit tests for chat toggle suppression.
- Unit tests for channel/agent mapping.

## Open Questions
- Which gateway event types can provide `Intent` or `Channel` today?
- Should agent-based rules be skipped when `Agent` is missing, or should `Channel` be used as a fallback for agent-based rules?
