using System.Collections.Generic;
using Xunit;
using OpenClaw.Shared;

namespace OpenClaw.Shared.Tests;

public class NotificationCategorizerTests
{
    private readonly NotificationCategorizer _categorizer = new();

    // --- Keyword fallback (backward compatibility) ---

    [Theory]
    [InlineData("Your blood sugar is high", "health")]
    [InlineData("Glucose level: 180 mg/dl", "health")]
    [InlineData("CGM reading available", "health")]
    [InlineData("URGENT: Action required", "urgent")]
    [InlineData("This is critical", "urgent")]
    [InlineData("Emergency situation", "urgent")]
    [InlineData("Reminder: Meeting at 3pm", "reminder")]
    [InlineData("Item is in stock", "stock")]
    [InlineData("Available now!", "stock")]
    [InlineData("New email in inbox", "email")]
    [InlineData("Gmail notification", "email")]
    [InlineData("Meeting starting soon", "calendar")]
    [InlineData("Calendar event: Team standup", "calendar")]
    [InlineData("Build failed", "error")]
    [InlineData("Exception occurred", "error")]
    [InlineData("Build succeeded", "build")]
    [InlineData("CI pipeline completed", "build")]
    [InlineData("Deploy finished", "build")]
    [InlineData("Hello world", "info")]
    public void KeywordFallback_BackwardCompatible(string message, string expectedType)
    {
        var notification = new OpenClawNotification { Message = message };
        var (_, type) = _categorizer.Classify(notification);
        Assert.Equal(expectedType, type);
    }

    [Fact]
    public void KeywordFallback_IsCaseInsensitive()
    {
        var notification = new OpenClawNotification { Message = "URGENT: test" };
        Assert.Equal("urgent", _categorizer.Classify(notification).type);

        notification = new OpenClawNotification { Message = "urgent: test" };
        Assert.Equal("urgent", _categorizer.Classify(notification).type);
    }

    // --- Structured metadata takes priority ---

    [Fact]
    public void Intent_TakesPriority_OverKeywords()
    {
        // Message says "email" but intent says "build"
        var notification = new OpenClawNotification
        {
            Message = "New email notification",
            Intent = "build"
        };
        var (_, type) = _categorizer.Classify(notification);
        Assert.Equal("build", type);
    }

    [Theory]
    [InlineData("health", "health")]
    [InlineData("urgent", "urgent")]
    [InlineData("alert", "urgent")]
    [InlineData("reminder", "reminder")]
    [InlineData("email", "email")]
    [InlineData("calendar", "calendar")]
    [InlineData("build", "build")]
    [InlineData("stock", "stock")]
    [InlineData("error", "error")]
    public void Intent_MapsCorrectly(string intent, string expectedType)
    {
        var notification = new OpenClawNotification { Message = "test", Intent = intent };
        Assert.Equal(expectedType, _categorizer.Classify(notification).type);
    }

    [Fact]
    public void Channel_TakesPriority_OverKeywords()
    {
        // Message says "email" but channel is "calendar"
        var notification = new OpenClawNotification
        {
            Message = "Check your email",
            Channel = "calendar"
        };
        Assert.Equal("calendar", _categorizer.Classify(notification).type);
    }

    [Theory]
    [InlineData("calendar", "calendar")]
    [InlineData("email", "email")]
    [InlineData("ci", "build")]
    [InlineData("build", "build")]
    [InlineData("stock", "stock")]
    [InlineData("inventory", "stock")]
    [InlineData("health", "health")]
    [InlineData("alerts", "urgent")]
    public void Channel_MapsCorrectly(string channel, string expectedType)
    {
        var notification = new OpenClawNotification { Message = "test", Channel = channel };
        Assert.Equal(expectedType, _categorizer.Classify(notification).type);
    }

    [Fact]
    public void Intent_TakesPriority_OverChannel()
    {
        var notification = new OpenClawNotification
        {
            Message = "test",
            Channel = "email",
            Intent = "build"
        };
        Assert.Equal("build", _categorizer.Classify(notification).type);
    }

    [Fact]
    public void UnknownChannel_FallsThrough_ToKeywords()
    {
        var notification = new OpenClawNotification
        {
            Message = "Your blood sugar is high",
            Channel = "unknown-channel"
        };
        Assert.Equal("health", _categorizer.Classify(notification).type);
    }

    [Fact]
    public void UnknownIntent_FallsThrough_ToChannel()
    {
        var notification = new OpenClawNotification
        {
            Message = "test",
            Intent = "unknown-intent",
            Channel = "email"
        };
        Assert.Equal("email", _categorizer.Classify(notification).type);
    }

    // --- User-defined rules ---

    [Fact]
    public void UserRule_KeywordMatch_Categorizes()
    {
        var rules = new List<UserNotificationRule>
        {
            new() { Pattern = "invoice", Category = "email", Enabled = true }
        };
        var notification = new OpenClawNotification { Message = "New invoice received" };
        Assert.Equal("email", _categorizer.Classify(notification, rules).type);
    }

    [Fact]
    public void UserRule_RegexMatch_Categorizes()
    {
        var rules = new List<UserNotificationRule>
        {
            new() { Pattern = @"PR\s*#\d+", IsRegex = true, Category = "build", Enabled = true }
        };
        var notification = new OpenClawNotification { Message = "PR #42 merged" };
        Assert.Equal("build", _categorizer.Classify(notification, rules).type);
    }

    [Fact]
    public void UserRule_DisabledRule_IsSkipped()
    {
        var rules = new List<UserNotificationRule>
        {
            new() { Pattern = "invoice", Category = "email", Enabled = false }
        };
        var notification = new OpenClawNotification { Message = "New invoice received" };
        // Falls through to keyword "info" since no keyword matches
        Assert.Equal("info", _categorizer.Classify(notification, rules).type);
    }

    [Fact]
    public void UserRule_InvalidRegex_IsSkipped()
    {
        var rules = new List<UserNotificationRule>
        {
            new() { Pattern = "[invalid", IsRegex = true, Category = "build", Enabled = true }
        };
        var notification = new OpenClawNotification { Message = "Hello world" };
        Assert.Equal("info", _categorizer.Classify(notification, rules).type);
    }

    [Fact]
    public void UserRule_TakesPriority_OverKeywords()
    {
        // Message contains "email" keyword, but user rule maps "inbox" to "stock"
        var rules = new List<UserNotificationRule>
        {
            new() { Pattern = "inbox", Category = "stock", Enabled = true }
        };
        var notification = new OpenClawNotification { Message = "New items in your inbox" };
        Assert.Equal("stock", _categorizer.Classify(notification, rules).type);
    }

    [Fact]
    public void UserRule_StructuredMetadata_TakesPriority_OverUserRules()
    {
        var rules = new List<UserNotificationRule>
        {
            new() { Pattern = "anything", Category = "stock", Enabled = true }
        };
        var notification = new OpenClawNotification
        {
            Message = "anything here",
            Intent = "build"
        };
        Assert.Equal("build", _categorizer.Classify(notification, rules).type);
    }

    [Fact]
    public void UserRule_FirstMatchWins()
    {
        var rules = new List<UserNotificationRule>
        {
            new() { Pattern = "report", Category = "email", Enabled = true },
            new() { Pattern = "report", Category = "build", Enabled = true }
        };
        var notification = new OpenClawNotification { Message = "Daily report ready" };
        Assert.Equal("email", _categorizer.Classify(notification, rules).type);
    }

    [Fact]
    public void UserRule_MatchesAgainstTitleAndMessage()
    {
        var rules = new List<UserNotificationRule>
        {
            new() { Pattern = "special-title", Category = "urgent", Enabled = true }
        };
        var notification = new OpenClawNotification
        {
            Title = "special-title",
            Message = "generic message"
        };
        Assert.Equal("urgent", _categorizer.Classify(notification, rules).type);
    }

    // --- Pipeline order verification ---

    [Fact]
    public void PipelineOrder_Intent_Channel_UserRules_Keywords()
    {
        var rules = new List<UserNotificationRule>
        {
            new() { Pattern = "test", Category = "stock", Enabled = true }
        };

        // All layers match — intent wins
        var notification = new OpenClawNotification
        {
            Message = "test email urgent blood sugar",
            Intent = "calendar",
            Channel = "email"
        };
        Assert.Equal("calendar", _categorizer.Classify(notification, rules).type);

        // Remove intent — channel wins
        notification.Intent = null;
        Assert.Equal("email", _categorizer.Classify(notification, rules).type);

        // Remove channel — user rule wins
        notification.Channel = null;
        Assert.Equal("stock", _categorizer.Classify(notification, rules).type);

        // Remove user rules — keyword wins
        Assert.Equal("health", _categorizer.Classify(notification).type);
    }

    // --- ClassifyByKeywords static method ---

    [Fact]
    public void ClassifyByKeywords_DefaultsToInfo()
    {
        var (title, type) = NotificationCategorizer.ClassifyByKeywords("Hello world");
        Assert.Equal("info", type);
        Assert.Equal("🤖 OpenClaw", title);
    }

    [Fact]
    public void ClassifyByKeywords_ReturnsCorrectTitles()
    {
        Assert.Equal("🩸 Blood Sugar Alert", NotificationCategorizer.ClassifyByKeywords("blood sugar high").title);
        Assert.Equal("🚨 Urgent Alert", NotificationCategorizer.ClassifyByKeywords("urgent message").title);
        Assert.Equal("⏰ Reminder", NotificationCategorizer.ClassifyByKeywords("reminder").title);
        Assert.Equal("📦 Stock Alert", NotificationCategorizer.ClassifyByKeywords("in stock").title);
        Assert.Equal("📧 Email", NotificationCategorizer.ClassifyByKeywords("email notification").title);
        Assert.Equal("📅 Calendar", NotificationCategorizer.ClassifyByKeywords("calendar event").title);
        Assert.Equal("⚠️ Error", NotificationCategorizer.ClassifyByKeywords("error occurred").title);
        Assert.Equal("🔨 Build", NotificationCategorizer.ClassifyByKeywords("deploy finished").title);
    }

    // --- Empty/null edge cases ---

    [Fact]
    public void EmptyMessage_DefaultsToInfo()
    {
        var notification = new OpenClawNotification { Message = "" };
        Assert.Equal("info", _categorizer.Classify(notification).type);
    }

    [Fact]
    public void NullUserRules_FallsToKeywords()
    {
        var notification = new OpenClawNotification { Message = "urgent alert" };
        Assert.Equal("urgent", _categorizer.Classify(notification, null).type);
    }

    [Fact]
    public void EmptyUserRules_FallsToKeywords()
    {
        var notification = new OpenClawNotification { Message = "urgent alert" };
        Assert.Equal("urgent", _categorizer.Classify(notification, new List<UserNotificationRule>()).type);
    }

    [Fact]
    public void EmptyPattern_RuleIsSkipped()
    {
        var rules = new List<UserNotificationRule>
        {
            new() { Pattern = "", Category = "urgent", Enabled = true }
        };
        var notification = new OpenClawNotification { Message = "Hello world" };
        Assert.Equal("info", _categorizer.Classify(notification, rules).type);
    }
}
