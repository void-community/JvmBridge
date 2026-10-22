using JvmBridge.Native;

namespace JvmBridge.Tests.Agents;

/// <summary>An event notification that an agent enabled or disabled through a fake JVM.</summary>
/// <param name="Event">The JVMTI event.</param>
/// <param name="Enabled">Whether it was enabled; otherwise it was disabled.</param>
internal readonly record struct EventToggle(jvmtiEvent Event, bool Enabled);
