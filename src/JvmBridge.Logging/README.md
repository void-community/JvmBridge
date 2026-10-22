# JvmBridge.Logging

`Microsoft.Extensions.Logging` adapters for [JvmBridge](https://github.com/void-community/JvmBridge) agents.

- `new AgentLoggerProvider(context)` is an `ILoggerProvider`. Its loggers write to the agent's log, so code that takes an `ILogger` can run inside an agent. A line is dropped when it is below `context.LogLevel` (`-agentpath:...=log=debug`), and the default sink writes it to standard error prefixed with the agent's name.
- `AgentLogging.Redirect(logger)` sends everything JvmBridge logs, including failures that the runtime contained, to an `ILogger` instead of standard error. It replaces `AgentLog.Sink`; pass `null` to restore the default.

```csharp
public sealed class MyAgent : JavaAgent
{
    public override void Configure(AgentContext context)
    {
        using AgentLoggerProvider provider = new(context);
        ILogger logger = provider.CreateLogger("MyAgent");
        logger.LogInformation("configured with {Options}", context.Options);
    }
}
```

Agent log levels map to `LogLevel` as `Trace`, `Debug`, `Information`, `Warning`, `Error` (`Critical` logs as `Error`), and `None` drops everything.
