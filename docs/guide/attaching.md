# Loading and attaching an agent

An agent library is loaded by the JVM through one of four routes. Which entry point runs depends on the route: `-agentpath` and `JAVA_TOOL_OPTIONS` call `Agent_OnLoad` (your `OnLoad`), while the attach routes call `Agent_OnAttach` (your `OnAttach`, with `context.IsAttached` true). Everything below was run against HotSpot 8, 17 and 21 on linux-arm64 with the [**HelloAgent**](../../samples/HelloAgent) sample; OpenJ9 and other platforms are covered by the CI matrix rather than by these recipes.

Use the absolute path of the library built for the JVM process's operating system and architecture (`.so`, `.dll` or `.dylib`); see [**Publishing**](../../README.md#a-native-agent-in-a-few-lines).

## At launch

```sh
java -agentpath:/absolute/path/MyAgent.so=log=debug,mode=fast -jar application.jar
```

The text after `=` becomes `context.Options` (parse it with `context.ParsedOptions`). To load the agent without editing the command line, put the same option in `JAVA_TOOL_OPTIONS`:

```sh
JAVA_TOOL_OPTIONS="-agentpath:/absolute/path/MyAgent.so=log=debug" java -jar application.jar
```

Every JVM the environment variable reaches loads the agent, including build tools and test forks, and each prints `Picked up JAVA_TOOL_OPTIONS: ...` to standard error. Both routes behave identically on Java 8, 17 and 21.

## Into a running JVM

Find the process id with `jps -l` or `pgrep -f`, then use either route. The JVM must run as the same user as the tool.

**`jcmd`** (Java 9 and later):

```sh
jcmd <pid> JVMTI.agent_load /absolute/path/MyAgent.so log=debug
```

The word after the path is the agent option text. A successful load prints `return code: 0`. Java 8's `jcmd` does not know this command (`Unknown diagnostic command`), so use the Attach API there.

**The Attach API** (any Java version):

```java
import com.sun.tools.attach.VirtualMachine;

VirtualMachine machine = VirtualMachine.attach(pid);
try { machine.loadAgentPath("/absolute/path/MyAgent.so", "log=debug"); }
finally { machine.detach(); }
```

On Java 8 put `$JAVA_HOME/lib/tools.jar` on the class path of the attaching program; from Java 9 the `jdk.attach` module is part of the JDK.

Java 21 and later print a warning when an agent is loaded into a running JVM unless the target was started with `-XX:+EnableDynamicAgentLoading`:

```text
WARNING: A JVM TI agent has been loaded dynamically (/absolute/path/MyAgent.so)
WARNING: If a serviceability tool is in use, please run with -XX:+EnableDynamicAgentLoading to hide this warning
WARNING: Dynamic loading of agents will be disallowed by default in a future release
```

The agent still loads. Plan for the flag to become mandatory in a later Java release.

## Telling a running agent what to do

Loading the agent library again into a JVM where its agent already runs does not start a second copy. The runtime calls `OnCommand` on the running agent with the option text of the new load, on the JVM's attach listener thread. That makes the same two routes a control channel: the first load starts the agent, and later loads send it commands.

```csharp
public override void OnCommand(AgentContext context, string command)
{
    if (command == "off")
        context.Unpatch(Tracing);
    else
        throw new InvalidOperationException("Unknown command " + command);
}
```

```sh
jcmd <pid> JVMTI.agent_load /absolute/path/MyAgent.so off
```

A command that finishes makes `jcmd` print `return code: 0`. A command that throws is reported in the target's log (`JvmBridge: InvalidOperationException: Unknown command bogus`) and `jcmd` prints `return code: -1`. With the Attach API a failed command throws `AgentInitializationException: Agent_OnAttach failed`. An agent that does not override `OnCommand` logs that it ignored the command and the load succeeds.

This works the same whether the agent was started with `-agentpath`, `JAVA_TOOL_OPTIONS` or an earlier attach. It was run against HotSpot 8, 17 and 21 with the `unpatch` and `patch` commands of the HelloAgent sample.

## What an attached agent can do

An attached agent runs `OnAttach` with callbacks already active, after the VM-initialization event has passed. Registered patches are applied to classes that load afterwards, and, when the agent obtained the retransformation capability, to the matching classes that are already loaded. A retransformation can only change method bodies: it cannot add methods or fields, which the JVM forbids on a loaded class. See the [**limitations**](limitations.md).

## Choosing a route

| Need | Route |
|---|---|
| Instrument from the first class | `-agentpath` or `JAVA_TOOL_OPTIONS` |
| Instrument a JVM you cannot restart | `jcmd` (9+) or the Attach API |
| Change what a running agent does | the same route again, handled by `OnCommand` |
