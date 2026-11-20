namespace JvmBridge.Build.Models;

/// <summary>
/// The targets that CI builds and tests, the Java majors and implementations of its JVM grid, and the JVM options some cells need. The shared workflow of void-community/Actions builds the samples and runs the
/// tests for each runtime it has; a target it has not names the image its agent is cross-built in.
/// </summary>
internal static class Compatibility
{
    /// <summary>Gets the configuration, with the Java majors from 8 to the newest of the pinned inventory.</summary>
    internal static CompatibilityConfiguration Configuration { get; } = new()
    {
        Implementations = ["hotspot", "openj9"],
        JavaMajors = [.. Enumerable.Range(start: 8, PinnedJdks.NewestJava - 7)],
        Targets =
        [
            new()
            {
                Rid = "linux-x64",
                Os = "linux",
                Arch = "x64",
                Agent = true
            },
            new()
            {
                Rid = "linux-arm64",
                Os = "linux",
                Arch = "aarch64",
                Agent = true
            },
            new()
            {
                Rid = "linux-arm",
                Os = "linux",
                Arch = "arm",
                Agent = true,
            },
            new()
            {
                Rid = "linux-musl-x64",
                Os = "linux",
                Arch = "x64",
                Agent = true,
                Musl = true,
            },
            new()
            {
                Rid = "linux-musl-arm64",
                Os = "linux",
                Arch = "aarch64",
                Agent = true,
                Musl = true,
            },
            new()
            {
                Rid = "linux-musl-arm",
                Os = "linux",
                Arch = "arm",
                Build = true,
                Musl = true,
                CrossContainer = "mcr.microsoft.com/dotnet-buildtools/prereqs:azurelinux-3.0-net10.0-cross-arm-alpine@sha256:a295314210c0f21f8e7f37fb4510a3d14b8f54991a702cde92b532298c4c6826",
                Status = "experimental",
                Reason = "Native library cross-build and export checks run on AMD64; no matching Java SE musl ARM32 archive is currently configured."
            },
            new()
            {
                Rid = "win-x64",
                Os = "windows",
                Arch = "x64",
                Agent = true
            },
            new()
            {
                Rid = "win-arm64",
                Os = "windows",
                Arch = "aarch64",
                Agent = true
            },
            new()
            {
                Rid = "win-x86",
                Os = "windows",
                Arch = "x86",
                Agent = true
            },
            new()
            {
                Rid = "osx-x64",
                Os = "mac",
                Arch = "x64",
                Agent = true
            },
            new()
            {
                Rid = "osx-arm64",
                Os = "mac",
                Arch = "aarch64",
                Agent = true
            },
            new()
            {
                Rid = "linux-bionic-x64",
                Status = "experimental",
                Reason = "Android ART needs an Android application/emulator and ART-specific JVMTI validation; Java SE archives do not establish support."
            },
            new()
            {
                Rid = "linux-bionic-arm64",
                Status = "experimental",
                Reason = "Android ART needs an Android application/emulator and ART-specific JVMTI validation; Java SE archives do not establish support."
            },
            new()
            {
                Rid = "linux-bionic-arm",
                Status = "experimental",
                Reason = "Experimental NativeAOT Android ARM32 toolchain; ART-specific validation is required."
            },
            new()
            {
                Rid = "ios-arm64",
                Status = "unavailable",
                Reason = "No supported Java SE JVM and -agentpath loader in this target's standard environment."
            },
            new()
            {
                Rid = "iossimulator-arm64",
                Status = "unavailable",
                Reason = "No supported Java SE JVM and -agentpath loader in this target's standard environment."
            },
            new()
            {
                Rid = "iossimulator-x64",
                Status = "unavailable",
                Reason = "No supported Java SE JVM and -agentpath loader in this target's standard environment."
            },
            new()
            {
                Rid = "tvos-arm64",
                Status = "unavailable",
                Reason = "No supported Java SE JVM and -agentpath loader in this target's standard environment."
            },
            new()
            {
                Rid = "tvossimulator-arm64",
                Status = "unavailable",
                Reason = "No supported Java SE JVM and -agentpath loader in this target's standard environment."
            },
            new()
            {
                Rid = "tvossimulator-x64",
                Status = "unavailable",
                Reason = "No supported Java SE JVM and -agentpath loader in this target's standard environment."
            },
            new()
            {
                Rid = "maccatalyst-arm64",
                Status = "experimental",
                Reason = "Requires a Catalyst application and compatible embedded JVM; desktop macOS tests do not validate Catalyst."
            },
            new()
            {
                Rid = "maccatalyst-x64",
                Status = "experimental",
                Reason = "Requires a Catalyst application and compatible embedded JVM; desktop macOS tests do not validate Catalyst."
            },
        ],
        VmOptionRules =
        [
            new()
            {
                Java = 10,
                Implementation = "hotspot",
                Architecture = "x64",
                Options = ["-XX:UseAVX=2"],
                Reason = "Constrain the obsolete JVM vector instruction set on modern hosted CPUs; baseline and agent processes use identical settings."
            },
            .. new[] { 18, 19, 20 }.Select(static java => new VmOptionRule
                {
                    Java = java,
                    Implementation = "openj9",
                    Os = "mac",
                    Architecture = "aarch64",
                    Options = ["-Xnoaot"],
                    Reason = "Keep the ahead-of-time code of OpenJ9's shared classes cache out of the JVM: with it, a JVM that a host creates through JNI fails at random while it starts, in a plain C host too; baseline and agent processes use identical settings."
                }),
        ]
    };
}
