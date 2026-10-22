# Loading and attaching an agent

An agent library is loaded by the JVM through one of five routes. Which entry point runs depends on the route: `-agentpath` and `JAVA_TOOL_OPTIONS` call `Agent_OnLoad` (your `OnLoad`), while the attach routes and the `-javaagent` jar call `Agent_OnAttach` (your `OnAttach`, with `context.IsAttached` true). Everything below was run against HotSpot 8, 17 and 21 on linux-arm64 with the [**HelloAgent**](../../samples/HelloAgent) sample; OpenJ9 and other platforms are covered by the CI matrix rather than by these recipes.

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

## From a jar with `-javaagent`

`java -javaagent:agent.jar=options` is the form that build tools, containers and application servers already know how to pass. [**`jvmbridge pack`**](tool.md) builds such a jar from the published libraries of your agent, one per platform:

```sh
dotnet tool install --global JvmBridge.Tool
jvmbridge pack -o agent.jar --agent linux-x64=publish/linux-x64/MyAgent.so --agent win-x64=publish/win-x64/MyAgent.dll
java -javaagent:agent.jar=log=debug -jar application.jar
```

The jar carries a small Java launcher, compiled for Java 8, that the JVM runs as `premain`. It extracts the library of the platform it runs on into a private temporary directory, loads it with `System.load`, and the library starts your agent from `JNI_OnLoad`. The text after `=` becomes `context.Options`. On Linux and macOS the extracted file is deleted as soon as it is loaded, so even a crashing JVM leaves nothing behind; where the operating system refuses to delete a loaded library (Windows), the launcher removes it when the JVM exits. A JVM on a platform that the jar has no library for stops at startup with a message that lists the platforms the jar has.

The route was run against HotSpot 8, 17 and 21 on linux-arm64: the whole BridgeFixture harness of the HelloAgent sample passes through a jar, and `JAVA_TOOL_OPTIONS=-javaagent:agent.jar` loads the agent in the same way. The CI matrix runs the same jar scenario on every other platform and JVM.

Know what this route is. The launcher runs after the VM has started, so the agent starts as if it had been **attached**: `OnAttach` runs, not `OnLoad`; the capabilities are the ones HotSpot offers to an attached agent (no exception or method entry and exit events, see [**limitations**](limitations.md)); and classes that loaded before the launcher ran, such as the JDK's startup classes, are seen only when the agent retransforms them. Application classes load after the launcher, so patches apply to them as they load. An agent that needs the startup capabilities uses `-agentpath` instead.

Naming a jar twice starts its agent once: the second start prints `jvmbridge: <launcher> is already started in this JVM` and is ignored. Two different agent jars on one command line each get their own launcher class (`jvmbridge pack` names it after a hash of the libraries), which is what keeps the JVM from running the first jar's launcher for the second jar; they start two separate agents, see [**more than one agent**](recipes.md#run-more-than-one-agent-in-a-jvm).

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
| Instrument from the first class, with every capability | `-agentpath` or `JAVA_TOOL_OPTIONS` |
| Hand one file to a build tool, container or server | `-javaagent` with a jar from `jvmbridge pack` |
| Instrument a JVM you cannot restart | `jcmd` (9+) or the Attach API |
| Change what a running agent does | the same route again, handled by `OnCommand` |
