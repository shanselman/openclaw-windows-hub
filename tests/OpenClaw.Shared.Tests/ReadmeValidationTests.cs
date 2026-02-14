using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace OpenClaw.Shared.Tests;

public class ReadmeValidationTests
{
    [Fact]
    public void ReadmeAllowCommandsJsonExample_IsValid()
    {
        // This test validates that the JSON example in the README for configuring
        // allowCommands is valid and properly formatted. This prevents users from
        // encountering "Invalid config" errors when following the documentation.

        var readmePath = Path.Combine(GetRepositoryRoot(), "README.md");
        var readmeContent = File.ReadAllText(readmePath);

        // Extract the allowCommands JSON example from the README
        // It's between lines that contain "Configure gateway allowCommands" and the warning about wildcards
        var jsonPattern = @"```json\s*(\{[\s\S]*?\})\s*```";
        var matches = Regex.Matches(readmeContent, jsonPattern);
        
        Assert.True(matches.Count > 0, "No JSON code blocks found in README");

        // Find the allowCommands configuration example
        var allowCommandsJson = matches
            .Select(m => m.Groups[1].Value)
            .FirstOrDefault(json => json.Contains("allowCommands"));

        Assert.NotNull(allowCommandsJson);

        // Validate that it's valid JSON
        var exception = Record.Exception(() => JsonDocument.Parse(allowCommandsJson));
        Assert.Null(exception);

        // Parse and validate structure
        using var doc = JsonDocument.Parse(allowCommandsJson);
        var root = doc.RootElement;
        
        Assert.True(root.TryGetProperty("nodes", out var nodes), "JSON should have 'nodes' property");
        Assert.True(nodes.TryGetProperty("allowCommands", out var allowCommands), "nodes should have 'allowCommands' property");
        Assert.Equal(JsonValueKind.Array, allowCommands.ValueKind);
        
        // Validate that all array elements are strings
        foreach (var command in allowCommands.EnumerateArray())
        {
            Assert.Equal(JsonValueKind.String, command.ValueKind);
        }

        // Validate that the expected commands are present
        var commandStrings = allowCommands.EnumerateArray()
            .Select(e => e.GetString())
            .ToList();

        var expectedCommands = new[]
        {
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
        };

        foreach (var expected in expectedCommands)
        {
            Assert.Contains(expected, commandStrings);
        }
    }

    private static string GetRepositoryRoot()
    {
        // First, try environment variable (useful for CI/CD and test runners)
        var envRepoRoot = Environment.GetEnvironmentVariable("OPENCLAW_REPO_ROOT");
        if (!string.IsNullOrEmpty(envRepoRoot) && Directory.Exists(envRepoRoot))
        {
            return envRepoRoot;
        }

        // Fall back to walking up directory tree to find .git
        var currentDir = Directory.GetCurrentDirectory();
        while (currentDir != null && !Directory.Exists(Path.Combine(currentDir, ".git")))
        {
            currentDir = Directory.GetParent(currentDir)?.FullName;
        }
        return currentDir ?? throw new InvalidOperationException(
            "Could not find repository root. Set OPENCLAW_REPO_ROOT environment variable or ensure .git directory exists.");
    }
}
