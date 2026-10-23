using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace JvmBridge.Generator;

/// <summary>Generates a Java class with <c>native</c> methods, and the unmanaged entry points that implement them, for a class marked with <c>JavaClassAttribute</c>.</summary>
[Generator]
public sealed class NativeHelperGenerator : IIncrementalGenerator
{
    /// <inheritdoc/>
    private const string ClassVersionPlaceholder = "JVMBRIDGE_CLASS_VERSION";
    private const string EnvironmentType = "JvmBridge.Runtime.JavaEnvironment";
    private const string JavaMethodAttribute = "JvmBridge.Agents.JavaMethodAttribute";
    private const string JavaTypeAttribute = "JvmBridge.Agents.JavaTypeAttribute";

    private const string Pointer = "global::JvmBridge.Native._jobject*";

    private static readonly DiagnosticDescriptor InvalidClass = new(
        id: "JVMB002",
        title: "Invalid native helper class",
        messageFormat: "{0}",
        category: "JvmBridge",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    private static readonly DiagnosticDescriptor InvalidMethod = new(
        id: "JVMB003",
        title: "Unsupported native helper method signature",
        messageFormat: "{0}",
        category: "JvmBridge",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    private static readonly Dictionary<SpecialType, Mapped> Primitives = new()
    {
        [SpecialType.System_Boolean] = new Mapped(descriptor: "Z", native: "byte", fromJni: "{0} != 0", toJni: "(byte)({0} ? 1 : 0)"),
        [SpecialType.System_SByte] = new Mapped(descriptor: "B", native: "sbyte", fromJni: "{0}", toJni: "{0}"),
        [SpecialType.System_Char] = new Mapped(descriptor: "C", native: "ushort", fromJni: "(char){0}", toJni: "(ushort){0}"),
        [SpecialType.System_Int16] = new Mapped(descriptor: "S", native: "short", fromJni: "{0}", toJni: "{0}"),
        [SpecialType.System_Int32] = new Mapped(descriptor: "I", native: "int", fromJni: "{0}", toJni: "{0}"),
        [SpecialType.System_Int64] = new Mapped(descriptor: "J", native: "long", fromJni: "{0}", toJni: "{0}"),
        [SpecialType.System_Single] = new Mapped(descriptor: "F", native: "float", fromJni: "{0}", toJni: "{0}"),
        [SpecialType.System_Double] = new Mapped(descriptor: "D", native: "double", fromJni: "{0}", toJni: "{0}"),
    };

    // The Java array type of each primitive C# array: the descriptor of the element and the C# element type that the array helpers take.
    private static readonly Dictionary<SpecialType, KeyValuePair<string, string>> ArrayElements = new()
    {
        [SpecialType.System_Boolean] = new KeyValuePair<string, string>(key: "Z", value: "bool"),
        [SpecialType.System_Byte] = new KeyValuePair<string, string>(key: "B", value: "byte"),
        [SpecialType.System_SByte] = new KeyValuePair<string, string>(key: "B", value: "sbyte"),
        [SpecialType.System_Char] = new KeyValuePair<string, string>(key: "C", value: "char"),
        [SpecialType.System_Int16] = new KeyValuePair<string, string>(key: "S", value: "short"),
        [SpecialType.System_Int32] = new KeyValuePair<string, string>(key: "I", value: "int"),
        [SpecialType.System_Int64] = new KeyValuePair<string, string>(key: "J", value: "long"),
        [SpecialType.System_Single] = new KeyValuePair<string, string>(key: "F", value: "float"),
        [SpecialType.System_Double] = new KeyValuePair<string, string>(key: "D", value: "double"),
    };

    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<HelperOutput> helpers = context.SyntaxProvider.ForAttributeWithMetadataName(
            fullyQualifiedMetadataName: "JvmBridge.Agents.JavaClassAttribute",
            static (node, cancellationToken) => node is ClassDeclarationSyntax,
            static (syntax, cancellationToken) => Analyze(syntax)
        );

        // The helper class gets the class file version of JavaRelease, as the JavaSource classes do, so that a JVM of that release loads it: 50 for Java 6, 52 for Java 8.
        IncrementalValueProvider<int> classVersion = context.AnalyzerConfigOptionsProvider.Select(
            static (options, cancellationToken) => ClassVersionOf(options.GlobalOptions.TryGetValue(key: "build_property.JavaRelease", out string? release) ? release : null)
        );

        context.RegisterSourceOutput(
            helpers.Combine(classVersion),
            static (production, input) =>
            {
                HelperOutput helper = input.Left;

                foreach (Diagnostic diagnostic in helper.Diagnostics)
                    production.ReportDiagnostic(diagnostic);

                if (helper.Source is { } source)
                {
                    string versioned = source.Replace(ClassVersionPlaceholder, input.Right.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    production.AddSource(helper.HintName, SourceText.From(versioned, Encoding.UTF8));
                }
            }
        );
    }

    /// <summary>Gets the class file version of a Java release: 44 plus the release, 52 when the release is missing or not a number from 6 up.</summary>
    /// <param name="release">The value of JavaRelease.</param>
    /// <returns>The major version of the class file.</returns>
    internal static int ClassVersionOf(string? release)
    {
        bool parsed = int.TryParse(release, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int value);

        return parsed && value >= 6 ? 44 + value : 52;
    }

    private static HelperOutput Analyze(GeneratorAttributeSyntaxContext syntax)
    {
        INamedTypeSymbol type = (INamedTypeSymbol)syntax.TargetSymbol;
        Location location = type.Locations.FirstOrDefault() ?? Location.None;
        List<Diagnostic> diagnostics = [];
        string? javaName = ReadString(syntax.Attributes[index: 0]);

        if (javaName is not { Length: > 0 } || javaName.Contains(value: '.'))
        {
            diagnostics.Add(
                Diagnostic.Create(InvalidClass, location, messageArgs: ["The Java class name must be an internal name with slashes, for example pkg/Callbacks."])
            );
        }

        bool isPartial = type.DeclaringSyntaxReferences.Any(IsPartialDeclaration);

        if (!type.IsStatic || !isPartial || type.ContainingType is not null || type.IsGenericType)
        {
            diagnostics.Add(
                Diagnostic.Create(InvalidClass, location, messageArgs: ["A native helper class must be a top-level, non-generic static partial class."])
            );
        }

        List<NativeMethod> methods = [];

        foreach (IMethodSymbol method in type.GetMembers().OfType<IMethodSymbol>().Where(HasJavaMethodAttribute))
        {
            if (Map(method, diagnostics) is { } native)
                methods.Add(native);
        }

        if (methods.Count == 0 && diagnostics.Count == 0)
        {
            diagnostics.Add(
                Diagnostic.Create(InvalidClass, location, messageArgs: ["A native helper class needs at least one method marked with JavaMethodAttribute."])
            );
        }

        if (diagnostics.Count > 0 || javaName is null)
            return new HelperOutput(string.Empty, source: null, [.. diagnostics]);

        string prefix = type.ContainingNamespace.IsGlobalNamespace ? string.Empty : type.ContainingNamespace.ToDisplayString() + ".";

        return new HelperOutput(prefix + type.Name + ".JavaClass.g.cs", Emit(type, javaName, methods), []);
    }

    private static string ConvertArgument(NativeParameter parameter, int position)
    {
        return parameter.Type.FromJni.Replace(oldValue: "{0}", "argument" + position.ToString(CultureInfo.InvariantCulture));
    }

    private static string DeclareParameter(NativeParameter parameter, int position)
    {
        return ", " + parameter.Type.Native + " argument" + position.ToString(CultureInfo.InvariantCulture);
    }

    private static string Emit(INamedTypeSymbol type, string javaName, List<NativeMethod> methods)
    {
        string access = type.DeclaredAccessibility == Accessibility.Public ? "public" : "internal";
        string inNamespace = type.ContainingNamespace.IsGlobalNamespace ? string.Empty : type.ContainingNamespace.ToDisplayString();
        SourceWriter text = new();
        text.Line(text: "// <auto-generated/>");
        text.Line(text: "#nullable disable");

        if (inNamespace.Length > 0)
        {
            text.Line("namespace " + inNamespace);
            text.Line(text: "{");
        }

        text.Line(access + " static unsafe partial class " + type.Name);
        text.Line(text: "{");
        text.Line(
            text: "    /// <summary>Gets the Java class to define with <c>AgentContext.DefineClass</c>. Its native methods are registered when it is defined.</summary>"
        );
        text.Line("    " + access + " static global::JvmBridge.Agents.JavaClassDefinition Definition { get; } = CreateDefinition();");
        text.Line();
        text.Line(text: "    private static global::JvmBridge.Agents.JavaClassDefinition CreateDefinition()");
        text.Line(text: "    {");
        text.Line(
            "        global::JvmBridge.ClassFiles.ClassFileWriter writer = new(" + Literal(javaName) + ", majorVersion: " + ClassVersionPlaceholder + ");"
        );

        foreach (NativeMethod method in methods)
            text.Line("        writer = writer.AddNativeMethod(" + Literal(method.JavaName) + ", " + Literal(method.Descriptor) + ");");

        text.Line(
            "        return new global::JvmBridge.Agents.JavaClassDefinition(" + Literal(javaName) + ", writer.ToArray(), registerNatives: static (environment, type) =>"
        );
        text.Line(text: "        {");

        for (int index = 0; index < methods.Count; index++)
        {
            NativeMethod method = methods[index];
            string parameters = string.Concat(method.Parameters.Select(static parameter => parameter.Type.Native + ", "));
            string signature = "global::JvmBridge.Native.JNINativeInterface_**, " + Pointer + ", " + parameters + method.Return.Native;
            string pointer = "(nint)(delegate* unmanaged<" + signature + ">)&Native" + index.ToString(CultureInfo.InvariantCulture);
            text.Line(
                "            environment.RegisterNative(type, " + Literal(method.JavaName) + ", " + Literal(method.Descriptor) + ", " + pointer + ");"
            );
        }

        text.Line(text: "        });");
        text.Line(text: "    }");

        for (int index = 0; index < methods.Count; index++)
            EmitEntryPoint(text, type, methods[index], index);

        text.Line(text: "}");

        if (inNamespace.Length > 0)
            text.Line(text: "}");

        return text.ToString();
    }

    private static void EmitEntryPoint(SourceWriter text, INamedTypeSymbol type, NativeMethod method, int index)
    {
        string name = "Native" + index.ToString(CultureInfo.InvariantCulture);
        string parameters = string.Concat(method.Parameters.Select(DeclareParameter));
        string returnType = method.Return.IsVoid ? "void" : method.Return.Native;
        List<string> arguments = method.TakesEnvironment ? ["javaEnvironment"] : [];
        arguments.AddRange(method.Parameters.Select(ConvertArgument));
        string call = type.Name + "." + method.Name + "(" + string.Join(separator: ", ", arguments) + ")";
        string environment = "global::JvmBridge.Native.JNINativeInterface_** environment, " + Pointer + " type";
        text.Line();
        text.Line(text: "    [global::System.Runtime.InteropServices.UnmanagedCallersOnly]");
        text.Line("    private static " + returnType + " " + name + "(" + environment + parameters + ")");
        text.Line(text: "    {");
        text.Line(text: "        using global::JvmBridge.Runtime.JavaEnvironment javaEnvironment = new((nint)environment);");
        text.Line(text: "        try");
        text.Line(text: "        {");

        if (method.Return.IsVoid)
        {
            text.Line("            " + call + ";");
        }
        else
        {
            text.Line("            var value = " + call + ";");
            text.Line("            return " + method.Return.ToJni.Replace(oldValue: "{0}", newValue: "value") + ";");
        }

        text.Line(text: "        }");
        text.Line(text: "        catch (global::JvmBridge.Runtime.JavaThrowException throwing)");
        text.Line(text: "        {");
        text.Line(text: "            throwing.Raise(javaEnvironment);");
        text.Line(method.Return.IsVoid ? "            return;" : "            return default;");
        text.Line(text: "        }");
        text.Line(text: "        catch (global::System.Exception exception) when (global::JvmBridge.Agents.AgentRuntime.ReportFailure(exception))");
        text.Line(text: "        {");
        text.Line(method.Return.IsVoid ? "            return;" : "            return default;");
        text.Line(text: "        }");
        text.Line(text: "    }");
    }

    private static bool HasJavaMethodAttribute(IMethodSymbol method)
    {
        return method.GetAttributes().Any(static attribute => attribute.AttributeClass?.ToDisplayString() == JavaMethodAttribute);
    }

    private static bool IsPartialDeclaration(SyntaxReference reference)
    {
        return reference.GetSyntax() is ClassDeclarationSyntax declaration && declaration.Modifiers.IndexOf(SyntaxKind.PartialKeyword) >= 0;
    }

    private static string Literal(string value)
    {
        return SymbolDisplay.FormatLiteral(value, quote: true);
    }

    private static NativeMethod? Map(IMethodSymbol method, List<Diagnostic> diagnostics)
    {
        Location location = method.Locations.FirstOrDefault() ?? Location.None;
        AttributeData marker = method.GetAttributes().First(static attribute => attribute.AttributeClass?.ToDisplayString() == JavaMethodAttribute);
        string javaName = ReadString(marker) ?? ToJavaName(method.Name);

        if (!method.IsStatic || method.IsGenericMethod || method.IsAsync)
        {
            diagnostics.Add(Diagnostic.Create(InvalidMethod, location, $"{method.Name} must be a static, non-generic, non-async method."));

            return null;
        }

        List<NativeParameter> parameters = [];
        bool takesEnvironment = false;

        for (int index = 0; index < method.Parameters.Length; index++)
        {
            IParameterSymbol parameter = method.Parameters[index];

            if (index == 0 && parameter.Type.ToDisplayString() == EnvironmentType)
            {
                takesEnvironment = true;

                continue;
            }

            Mapped? mapped = parameter.RefKind == RefKind.None ? MapType(parameter.Type, parameter.GetAttributes()) : null;

            if (mapped is null)
            {
                Location parameterLocation = parameter.Locations.FirstOrDefault() ?? location;
                diagnostics.Add(
                    Diagnostic.Create(
                        InvalidMethod,
                        parameterLocation,
                        $"Parameter {parameter.Name} of {method.Name} has an unsupported type {parameter.Type.ToDisplayString()}."
                    )
                );

                return null;
            }

            parameters.Add(new NativeParameter(mapped));
        }

        Mapped? result = method.ReturnsVoid ? Mapped.Void : MapType(method.ReturnType, method.GetReturnTypeAttributes());

        if (result is null)
        {
            diagnostics.Add(Diagnostic.Create(InvalidMethod, location, $"{method.Name} returns an unsupported type {method.ReturnType.ToDisplayString()}."));

            return null;
        }

        return new NativeMethod(method.Name, javaName, takesEnvironment, parameters, result);
    }

    private static Mapped? MapArray(ITypeSymbol element)
    {
        if (element.SpecialType == SpecialType.System_String)
        {
            string toStrings = "{0} is null ? null : (" + Pointer + ")global::JvmBridge.Runtime.JavaArrays.NewStringArray(javaEnvironment, {0}).Release()";

            return new Mapped(
                descriptor: "[Ljava/lang/String;",
                Pointer,
                fromJni: "{0} == null ? null : global::JvmBridge.Runtime.JavaArrays.GetStringArray(javaEnvironment, (nint){0})",
                toStrings
            );
        }

        if (!ArrayElements.TryGetValue(element.SpecialType, out KeyValuePair<string, string> known))
            return null;

        string toArray = "{0} is null ? null : (" + Pointer + ")global::JvmBridge.Runtime.JavaArrays.NewArray<" + known.Value + ">(javaEnvironment, {0}).Release()";

        return new Mapped(
            "[" + known.Key,
            Pointer,
            "{0} == null ? null : global::JvmBridge.Runtime.JavaArrays.ToArray<" + known.Value + ">(javaEnvironment, (nint){0})",
            toArray
        );
    }

    private static Mapped? MapType(ITypeSymbol type, ImmutableArray<AttributeData> attributes)
    {
        if (Primitives.TryGetValue(type.SpecialType, out Mapped? primitive))
            return primitive;

        if (type.SpecialType == SpecialType.System_String)
        {
            string toJni = "{0} is null ? null : (" + Pointer + ")javaEnvironment.NewString({0}).Release()";

            return new Mapped(descriptor: "Ljava/lang/String;", Pointer, fromJni: "javaEnvironment.GetString((nint){0})", toJni);
        }

        if (type is IArrayTypeSymbol { Rank: 1 } array)
            return MapArray(array.ElementType);

        if (type.SpecialType != SpecialType.System_IntPtr)
            return null;

        AttributeData? declared = attributes.FirstOrDefault(static attribute => attribute.AttributeClass?.ToDisplayString() == JavaTypeAttribute);
        string descriptor = ReadString(declared) is { Length: > 0 } text ? text : "Ljava/lang/Object;";

        return new Mapped(descriptor, Pointer, fromJni: "(nint){0}", "(" + Pointer + "){0}");
    }

    private static string? ReadString(AttributeData? attribute)
    {
        if (attribute is null || attribute.ConstructorArguments.Length != 1)
            return null;

        string literal = attribute.ConstructorArguments[index: 0].ToCSharpString();
        bool quoted = literal.Length >= 2 && literal[index: 0] == '"' && literal[literal.Length - 1] == '"';

        return quoted ? literal.Substring(startIndex: 1, literal.Length - 2) : null;
    }

    private static string ToJavaName(string name)
    {
        return name.Length == 0 ? name : char.ToLowerInvariant(name[index: 0]).ToString() + name.Substring(startIndex: 1);
    }

    private sealed class HelperOutput(string hintName, string? source, ImmutableArray<Diagnostic> diagnostics)
    {
        public ImmutableArray<Diagnostic> Diagnostics { get; } = diagnostics;

        public string HintName { get; } = hintName;

        public string? Source { get; } = source;
    }

    private sealed class Mapped(string descriptor, string native, string fromJni, string toJni)
    {
        public static readonly Mapped Void = new(descriptor: "V", native: "void", string.Empty, string.Empty);

        public string Descriptor { get; } = descriptor;

        public string FromJni { get; } = fromJni;

        public bool IsVoid => Native == "void";

        public string Native { get; } = native;

        public string ToJni { get; } = toJni;
    }

    private sealed class NativeMethod(string name, string javaName, bool takesEnvironment, List<NativeParameter> parameters, Mapped result)
    {
        public string Descriptor => "(" + string.Concat(Parameters.Select(static parameter => parameter.Type.Descriptor)) + ")" + Return.Descriptor;

        public string JavaName { get; } = javaName;

        public string Name { get; } = name;

        public List<NativeParameter> Parameters { get; } = parameters;

        public Mapped Return { get; } = result;

        public bool TakesEnvironment { get; } = takesEnvironment;
    }

    private sealed class NativeParameter(Mapped type)
    {
        public Mapped Type { get; } = type;
    }
}
