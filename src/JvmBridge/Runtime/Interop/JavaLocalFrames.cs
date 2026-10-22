using JvmBridge.Native;

namespace JvmBridge.Runtime;

/// <summary>Local reference capacity and frames for a <see cref="JavaEnvironment"/>.</summary>
public static unsafe class JavaLocalFrames
{
    /// <summary>Makes sure the current thread can create at least this many more local references.</summary>
    /// <param name="environment">The JNI environment of the current thread.</param>
    /// <param name="capacity">The number of local references that will be needed.</param>
    public static void EnsureLocalCapacity(this JavaEnvironment environment, int capacity)
    {
        ArgumentNullException.ThrowIfNull(environment);
        environment.EnsureAccessible();
        int result = environment.Functions->EnsureLocalCapacity((JNINativeInterface_**)environment.Handle, capacity);
        environment.ThrowIfException(nameof(EnsureLocalCapacity));

        if (result != 0)
            throw new JniException(result, operation: nameof(EnsureLocalCapacity));
    }

    /// <summary>Opens a local reference frame with room for this many references.</summary>
    /// <param name="environment">The JNI environment of the current thread.</param>
    /// <param name="capacity">The number of local references that will be created in the frame.</param>
    /// <returns>The frame; dispose it, or call <see cref="JavaLocalFrame.Pop"/>, to release its references.</returns>
    public static JavaLocalFrame PushLocalFrame(this JavaEnvironment environment, int capacity)
    {
        ArgumentNullException.ThrowIfNull(environment);
        environment.EnsureAccessible();
        int result = environment.Functions->PushLocalFrame((JNINativeInterface_**)environment.Handle, capacity);
        environment.ThrowIfException(nameof(PushLocalFrame));

        if (result != 0)
            throw new JniException(result, operation: nameof(PushLocalFrame));

        return new JavaLocalFrame(environment);
    }
}
