namespace JvmBridge.Testing;

/// <summary>What an installed JDK says it is: the major version of its Java specification and its JVM implementation.</summary>
/// <param name="Major">The Java major version, 8 for a specification version of <c>1.8</c>.</param>
/// <param name="Implementation">The JVM implementation, <c>openj9</c> for an OpenJ9 VM and <c>hotspot</c> otherwise.</param>
internal sealed record InstalledJdk(int Major, string Implementation)
{
    private const string SpecificationVersion = "java.specification.version = ";
    private const string VirtualMachineName = "java.vm.name = ";

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"Java {Major.ToString(System.Globalization.CultureInfo.InvariantCulture)} ({Implementation})";
    }

    /// <summary>Reads the output of <c>java -XshowSettings:properties -version</c>, which every JDK from Java 8 prints, unlike the <c>release</c> file that some builds of Java 8 lack.</summary>
    /// <param name="output">The combined output of the command.</param>
    /// <returns>The JDK, or <see langword="null"/> when the output does not say.</returns>
    internal static InstalledJdk? Parse(string output)
    {
        string? version = null;
        string? name = null;

        foreach (string line in output.Split(separator: '\n'))
        {
            string trimmed = line.Trim();

            if (trimmed.StartsWith(SpecificationVersion, StringComparison.Ordinal))
                version = trimmed[SpecificationVersion.Length..];
            else if (trimmed.StartsWith(VirtualMachineName, StringComparison.Ordinal))
                name = trimmed[VirtualMachineName.Length..];
        }

        if (version is null || name is null)
            return null;

        // Java 8 and earlier report 1.<major>; Java 9 and later report the major itself.
        string majorText = version.StartsWith(value: "1.", StringComparison.Ordinal) ? version[2..] : version;
        int dot = majorText.IndexOf(value: '.', StringComparison.Ordinal);

        string digits = dot < 0 ? majorText : majorText[..dot];
        bool parsed = int.TryParse(digits, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int major);

        if (!parsed)
            return null;

        string implementation = name.Contains(value: "OpenJ9", StringComparison.OrdinalIgnoreCase) ? "openj9" : "hotspot";

        return new InstalledJdk(major, implementation);
    }
}
