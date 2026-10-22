namespace JvmBridge.Agents;

/// <summary>A Java exception that was thrown, as <see cref="JavaAgent.OnException"/> receives it. The references belong to the callback.</summary>
/// <param name="Thread">The thread that threw, as a callback-local reference.</param>
/// <param name="Method">The raw method ID of the method that threw.</param>
/// <param name="Location">The bytecode index in <paramref name="Method"/>.</param>
/// <param name="Exception">The exception object, as a callback-local reference.</param>
/// <param name="CatchMethod">The raw method ID of the method whose handler will catch it, or 0 when no handler is known in that frame.</param>
/// <param name="CatchLocation">The bytecode index of that handler.</param>
public readonly record struct JavaExceptionEvent(nint Thread, nint Method, long Location, nint Exception, nint CatchMethod, long CatchLocation);
