using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace JvmBridge.Generator;

/// <summary>
/// Generates unmanaged JVM agent entry points for a concrete Java agent implementation.
/// </summary>
[Generator]
public sealed class AgentGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor InvalidAgent = new(
        id: "JVMB001",
        title: "Invalid JVM agent",
        messageFormat: "{0}",
        category: "JvmBridge",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<INamedTypeSymbol> agents = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, cancellationToken) => node is ClassDeclarationSyntax declaration && declaration.BaseList != null,
            static (syntax, cancellation) => syntax.SemanticModel.GetDeclaredSymbol(syntax.Node, cancellation) as INamedTypeSymbol
        )
            .Where(static type => type != null && !type.IsAbstract && DerivesAgent(type))
            .Select(static (type, cancellationToken) => type ?? throw new InvalidOperationException(message: "Missing agent symbol."));

        context.RegisterSourceOutput(
            agents.Collect().Combine(context.CompilationProvider).Combine(context.AnalyzerConfigOptionsProvider),
            static (production, input) =>
            {
                HashSet<INamedTypeSymbol> distinctTypes = new(SymbolEqualityComparer.Default);

                foreach (INamedTypeSymbol candidate in input.Left.Left)
                {
                    if (!distinctTypes.Add(candidate))
                        continue;
                }

                ImmutableArray<INamedTypeSymbol> types = [.. distinctTypes];
                bool agentEnabled = input.Right.GlobalOptions.TryGetValue(key: "build_property.JvmBridgeAgent", out string? setting) && string.Equals(setting, b: "true", StringComparison.OrdinalIgnoreCase);

                if (types.Length == 0 && !agentEnabled)
                    return;

                if (types.Length != 1)
                {
                    production.ReportDiagnostic(
                        Diagnostic.Create(InvalidAgent, Location.None, messageArgs: ["Exactly one concrete JavaAgent subclass is required per native library."])
                    );

                    return;
                }

                INamedTypeSymbol type = types[index: 0];
                bool derivesAgent = DerivesAgent(type);
                bool accessible = input.Left.Right.IsSymbolAccessibleWithin(type, input.Left.Right.Assembly) && !type.IsFileLocal;

                for (INamedTypeSymbol? current = type; current != null; current = current.ContainingType)
                    accessible &= current.Arity == 0 && !current.IsFileLocal;

                bool constructor = type.InstanceConstructors.Any(value => value.Parameters.Length == 0 && input.Left.Right.IsSymbolAccessibleWithin(value, input.Left.Right.Assembly));

                if (!derivesAgent || type.IsAbstract || !accessible || !constructor)
                {
                    production.ReportDiagnostic(
                        Diagnostic.Create(
                            InvalidAgent,
                            type.Locations.FirstOrDefault(),
                            messageArgs: ["Agent must derive from JavaAgent, be non-generic and non-abstract, and have an accessible parameterless constructor."]
                        )
                    );

                    return;
                }

                if (!agentEnabled)
                {
                    production.ReportDiagnostic(
                        Diagnostic.Create(InvalidAgent, type.Locations.FirstOrDefault(), messageArgs: ["Set <JvmBridgeAgent>true</JvmBridgeAgent> in the agent project."])
                    );

                    return;
                }

                foreach (INamedTypeSymbol candidate in AllTypes(input.Left.Right.Assembly.GlobalNamespace))
                {
                    foreach (IMethodSymbol method in candidate.GetMembers().OfType<IMethodSymbol>())
                    {
                        foreach (AttributeData attribute in method.GetAttributes())
                        {
                            if (attribute.AttributeClass?.ToDisplayString() == "System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute")
                            {
                                foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
                                {
                                    bool isGeneratedEntryPoint = argument.Key == "EntryPoint" && IsGeneratedEntryPoint(argument.Value);

                                    if (isGeneratedEntryPoint)
                                    {
                                        production.ReportDiagnostic(
                                            Diagnostic.Create(
                                                InvalidAgent,
                                                method.Locations.FirstOrDefault(),
                                                messageArgs: ["Agent entry points are generated; remove conflicting manual exports."]
                                            )
                                        );

                                        return;
                                    }
                                }
                            }
                        }
                    }
                }

                string agent = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                string source = """
                // <auto-generated/>
                #nullable enable
                namespace JvmBridge.Generated
                {
                    internal static unsafe class NativeAgentExports
                    {
                        [global::System.Runtime.InteropServices.UnmanagedCallersOnly(EntryPoint = "Agent_OnLoad")]
                        public static int Load(nint machine, nint options, nint reserved)
                            => global::JvmBridge.Agents.AgentRuntime.Start(static () => new AGENT_TYPE(), machine, options, false, (nint)(delegate* unmanaged<nint, nint, nint, int>)&Load, AGENT_NAME);
                        [global::System.Runtime.InteropServices.UnmanagedCallersOnly(EntryPoint = "Agent_OnAttach")]
                        public static int Attach(nint machine, nint options, nint reserved)
                            => global::JvmBridge.Agents.AgentRuntime.Start(static () => new AGENT_TYPE(), machine, options, true, (nint)(delegate* unmanaged<nint, nint, nint, int>)&Attach, AGENT_NAME);
                        [global::System.Runtime.InteropServices.UnmanagedCallersOnly(EntryPoint = "Agent_OnUnload")]
                        public static void Unload(nint machine) => global::JvmBridge.Agents.AgentRuntime.Stop(machine);
                        [global::System.Runtime.InteropServices.UnmanagedCallersOnly(EntryPoint = "JNI_OnLoad")]
                        public static int LoadLibrary(nint machine, nint reserved)
                            => global::JvmBridge.Agents.AgentRuntime.StartFromLoadedLibrary(static () => new AGENT_TYPE(), machine, (nint)(delegate* unmanaged<nint, nint, int>)&LoadLibrary, AGENT_NAME);
                    }
                }
                """;

                production.AddSource(
                    hintName: "NativeAgentExports.g.cs",
                    SourceText.From(
                        source.Replace(oldValue: "AGENT_TYPE", agent).Replace(oldValue: "AGENT_NAME", SymbolDisplay.FormatLiteral(input.Left.Right.AssemblyName ?? type.Name, quote: true)),
                        Encoding.UTF8
                    )
                );
            }
        );
    }

    private static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceSymbol scope)
    {
        foreach (INamedTypeSymbol type in scope.GetTypeMembers())
        {
            yield return type;

            foreach (INamedTypeSymbol nested in NestedTypes(type))
                yield return nested;
        }

        foreach (INamespaceSymbol child in scope.GetNamespaceMembers())
        {
            foreach (INamedTypeSymbol type in AllTypes(child))
                yield return type;
        }
    }

    private static bool DerivesAgent(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? parent = type.BaseType; parent != null; parent = parent.BaseType)
        {
            if (parent.ToDisplayString() == "JvmBridge.Agents.JavaAgent")
                return true;
        }

        return false;
    }

    private static bool IsGeneratedEntryPoint(TypedConstant entryPoint)
    {
        string sourceValue = entryPoint.ToCSharpString();

        return sourceValue is "\"Agent_OnLoad\"" or "\"Agent_OnAttach\"" or "\"Agent_OnUnload\"" or "\"JNI_OnLoad\"";
    }

    private static IEnumerable<INamedTypeSymbol> NestedTypes(INamedTypeSymbol parent)
    {
        foreach (INamedTypeSymbol type in parent.GetTypeMembers())
        {
            yield return type;

            foreach (INamedTypeSymbol nested in NestedTypes(type))
                yield return nested;
        }
    }
}
