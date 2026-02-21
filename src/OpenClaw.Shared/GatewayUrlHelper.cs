using System;

namespace OpenClaw.Shared;

public static class GatewayUrlHelper
{
    public const string ValidationMessage = "Gateway URL must be a valid URL (ws://, wss://, http://, or https://).";

    public static bool IsValidGatewayUrl(string? gatewayUrl) =>
        TryNormalizeWebSocketUrl(gatewayUrl, out _);

    public static string NormalizeForWebSocket(string? gatewayUrl) =>
        TryNormalizeWebSocketUrl(gatewayUrl, out var normalizedUrl)
            ? normalizedUrl
            : gatewayUrl?.Trim() ?? string.Empty;

    public static string? ExtractCredentials(string gatewayUrl)
    {
        if (string.IsNullOrWhiteSpace(gatewayUrl))
        {
            return null;
        }

        if (!Uri.TryCreate(gatewayUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        return string.IsNullOrEmpty(uri.UserInfo) ? null : uri.UserInfo;
    }

    public static bool TryNormalizeWebSocketUrl(string? gatewayUrl, out string normalizedUrl)
    {
        normalizedUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(gatewayUrl))
        {
            return false;
        }

        var trimmed = gatewayUrl.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme.Equals("ws", StringComparison.OrdinalIgnoreCase) ||
            uri.Scheme.Equals("wss", StringComparison.OrdinalIgnoreCase))
        {
            normalizedUrl = trimmed;
            return true;
        }

        var schemeSeparator = trimmed.IndexOf("://", StringComparison.Ordinal);
        if (schemeSeparator < 0)
        {
            return false;
        }

        var remainder = trimmed.Substring(schemeSeparator);
        if (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
        {
            normalizedUrl = "ws" + remainder;
            return true;
        }

        if (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
        {
            normalizedUrl = "wss" + remainder;
            return true;
        }

        return false;
    }
}

