using JvmBridge.Agents;

using Microsoft.Extensions.Logging;

using Xunit;

namespace JvmBridge.Logging.Tests;

/// <summary>Verifies the ILogger that writes to the agent's log.</summary>
public sealed class AgentLoggerTests
{
    /// <summary>An empty category adds no prefix.</summary>
    [Fact]
    public void AnEmptyCategoryAddsNoPrefix()
    {
        List<string> lines = [];
        AgentLogger logger = new(string.Empty, level => true, (level, message) => lines.Add(message));

        logger.Log(LogLevel.Error, new EventId(id: 1), state: "bare", exception: null, Format);

        Assert.Equal(["bare"], lines);
    }

    /// <summary>Levels the context drops are reported disabled and never formatted.</summary>
    [Fact]
    public void DisabledLevelsAreNotFormatted()
    {
        List<string> lines = [];
        int formatted = 0;
        AgentLogger logger = new(category: "category", level => level >= AgentLogLevel.Warning, (level, message) => lines.Add(message));

        logger.Log(LogLevel.Debug, new EventId(id: 1), state: "state", exception: null, Count);
        logger.Log(LogLevel.Error, new EventId(id: 2), state: "state", exception: null, Format);

        Assert.False(logger.IsEnabled(LogLevel.Information));
        Assert.True(logger.IsEnabled(LogLevel.Critical));
        Assert.Equal(expected: 0, formatted);
        Assert.Equal(["category: state"], lines);

        string Count(string state, Exception? exception)
        {
            formatted++;

            return state;
        }
    }

    /// <summary>The level mapping round-trips, Critical logs as Error, and None stays None.</summary>
    [Fact]
    public void MapsLevels()
    {
        AgentLogLevel[] levels = [AgentLogLevel.Trace, AgentLogLevel.Debug, AgentLogLevel.Information, AgentLogLevel.Warning, AgentLogLevel.Error, AgentLogLevel.None];

        foreach (AgentLogLevel level in levels)
            Assert.Equal(level, AgentLogLevels.ToAgentLevel(AgentLogLevels.ToLogLevel(level)));

        Assert.Equal(AgentLogLevel.Error, AgentLogLevels.ToAgentLevel(LogLevel.Critical));
        Assert.Equal(AgentLogLevel.None, AgentLogLevels.ToAgentLevel(LogLevel.None));
    }

    /// <summary>A line is prefixed with its category, mapped to the agent level and followed by the exception text.</summary>
    [Fact]
    public void WritesCategoryLevelAndException()
    {
        List<string> lines = [];
        AgentLogger logger = new(category: "Acme.Service", level => true, (level, message) => lines.Add(level.ToString() + "|" + message));

        logger.Log(LogLevel.Warning, new EventId(id: 2), state: "failed", new InvalidOperationException(message: "boom"), Format);
        logger.Log(LogLevel.Information, new EventId(id: 3), state: "plain", exception: null, Format);

        Assert.Equal(expected: 2, lines.Count);
        Assert.StartsWith(expectedStartString: "Warning|Acme.Service: failed", lines[index: 0], StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "InvalidOperationException: boom", lines[index: 0], StringComparison.Ordinal);
        Assert.Equal(expected: "Information|Acme.Service: plain", lines[index: 1]);
    }

    private static string Format(string state, Exception? exception)
    {
        return state;
    }
}
