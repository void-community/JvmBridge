namespace JvmBridge.Agents;

/// <summary>A loaded class that a patch targets: its internal name and a JNI reference to it that the caller owns. It is a class so that code over lists of candidates shares one native instantiation.</summary>
/// <param name="Name">The internal class name.</param>
/// <param name="Type">The raw local class reference.</param>
internal sealed record RetransformCandidate(string Name, nint Type);
