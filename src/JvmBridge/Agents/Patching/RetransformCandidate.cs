namespace JvmBridge.Agents;

/// <summary>A loaded class that a patch targets: its internal name and a JNI reference to it that the caller owns.</summary>
/// <param name="Name">The internal class name.</param>
/// <param name="Type">The raw local class reference.</param>
internal readonly record struct RetransformCandidate(string Name, nint Type);
