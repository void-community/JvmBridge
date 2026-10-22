using JvmBridge.Runtime;

namespace JvmBridge.Agents;

/// <summary>Keeps one <see cref="JavaEnvironment"/> per thread for event callbacks, so that frequent events such as method entry do not allocate one each time.</summary>
internal static class ThreadEnvironment
{
    [ThreadStatic]
    private static JavaEnvironment? s_cached;

    internal static JavaEnvironment For(nint environment)
    {
        JavaEnvironment? cached = s_cached;

        if (cached is not null && cached.Handle == environment)
            return cached;

        cached = new JavaEnvironment(environment);
        s_cached = cached;

        return cached;
    }
}
