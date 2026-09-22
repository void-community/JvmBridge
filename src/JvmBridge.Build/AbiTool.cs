using JvmBridge.Build.Infrastructure;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using System.Text.RegularExpressions;

namespace JvmBridge.Build;

internal sealed partial class AbiTool(RepositoryContext repository)
{
    private static readonly string[] TableStructures = ["JNINativeInterface_", "JNIInvokeInterface_", "jvmtiInterface_1_", "jvmtiEventCallbacks", "jvmtiHeapCallbacks"];
    private static readonly string[] Capabilities = ["can_tag_objects", "can_retransform_classes", "can_generate_all_class_hook_events"];
    private readonly RepositoryContext _repository = repository;

    public static void Verify(IReadOnlyDictionary<string, long> native, IReadOnlyDictionary<string, long> managed)
    {
        List<string> failures = [];

        foreach ((string key, long value) in native)
        {
            if (key == "pointer")
                continue;

            if (key is "size.JNINativeInterface_" or "size.jvmtiInterface_1_" or "size.jvmtiEventCallbacks")
            {
                bool found = managed.TryGetValue(key, out long managedValue);

                if (!found || value > managedValue)
                {
                    string managedText = found ? managedValue.ToString(System.Globalization.CultureInfo.InvariantCulture) : "missing";
                    failures.Add($"{key}: native {value.ToString(System.Globalization.CultureInfo.InvariantCulture)} exceeds managed {managedText}");
                }
            }
            else
            {
                bool found = managed.TryGetValue(key, out long managedValue);

                if (!found || managedValue != value)
                {
                    string managedText = found ? managedValue.ToString(System.Globalization.CultureInfo.InvariantCulture) : "missing";
                    failures.Add($"{key}: native={value.ToString(System.Globalization.CultureInfo.InvariantCulture)}, managed={managedText}");
                }
            }
        }

        if (failures.Count > 0)
            throw new InvalidOperationException("ABI mismatch:\n" + string.Join(separator: '\n', failures));
    }

    public string CreateProbeSource(string headers)
    {
        string jni = File.ReadAllText(Path.Combine(headers, path2: "jni.h"));
        string tooling = File.ReadAllText(Path.Combine(headers, path2: "jvmti.h"));

        List<string> lines = [
            "#include <stdio.h>",
            "#include <stddef.h>",
            "#include <string.h>",
            "#include <jni.h>",
            "#include <jvmti.h>",
            "#ifdef _WIN32",
            "#include <windows.h>",
            "#define LOAD(path) LoadLibraryA(path)",
            "#define SYMBOL(handle,name) GetProcAddress(handle,name)",
            "#else",
            "#include <dlfcn.h>",
            "#define LOAD(path) dlopen(path,RTLD_NOW|RTLD_GLOBAL)",
            "#define SYMBOL(handle,name) dlsym(handle,name)",
            "#endif",
            "static int invocation(const char* path) {",
            "void* library = (void*)LOAD(path); if (!library) return 2;",
            "jint (JNICALL *create)(JavaVM**,void**,void*) = (jint (JNICALL *)(JavaVM**,void**,void*))SYMBOL(library,\"JNI_CreateJavaVM\");",
            "if (!create) return 3;",
            "JavaVM* vm = NULL; JNIEnv* env = NULL; JavaVMInitArgs args = {0};",
            "JavaVMOption option = {\"-Xcheck:jni\",NULL}; args.version=JNI_VERSION_1_8; args.nOptions=1; args.options=&option;",
            "jint created=create(&vm,(void**)&env,&args); if (created != 0) return 4;",
            "jint destroyed=(*vm)->DestroyJavaVM(vm);",
            "printf(\"{\\\"create\\\":%d,\\\"destroy\\\":%d}\\n\",created,destroyed); return 0;",
            "}",
            "int main(int argc, char **argv) {",
            "if (argc == 2) return invocation(argv[1]);",
            "printf(\"{\\\"pointer\\\":%llu\", (unsigned long long)sizeof(void*));"
        ];

        string toolingName = tooling.Contains(value: "struct JVMTINativeInterface_", StringComparison.Ordinal) ? "JVMTINativeInterface_" : "jvmtiInterface_1_";
        Dictionary<string, StructDeclarationSyntax> records = Records();

        foreach (string name in records.Keys)
        {
            string nativeName = name == "jvmtiInterface_1_" ? toolingName : name;

            if (!jni.Contains(nativeName, StringComparison.Ordinal) && !tooling.Contains(nativeName, StringComparison.Ordinal))
                continue;

            string ctype = name is "JNINativeInterface_" or "JNIInvokeInterface_" or "JNIEnv_" or "JavaVM_" or "jvmtiInterface_1_" or "_jvmtiEnv" ? "struct " + nativeName : nativeName;
            lines.Add($"printf(\",\\\"size.{name}\\\":%llu\", (unsigned long long)sizeof({ctype}));");
        }

        foreach ((string name, string header) in new[] { ("JNINativeInterface_", jni), ("JNIInvokeInterface_", jni), ("jvmtiInterface_1_", tooling) })
        {
            string nativeName = name == "jvmtiInterface_1_" ? toolingName : name;

            Match table = Regex.Match(
                header,
                "(?:typedef )?struct " + Regex.Escape(nativeName) + "\\s*\\{(.*?)\\n\\s*\\}",
                RegexOptions.Singleline | RegexOptions.CultureInvariant
            );

            if (!table.Success)
                throw new InvalidOperationException("Native table not found: " + name);

            foreach (Match fieldMatch in MyRegex().Matches(GroupValue(table, index: 1)))
            {
                Group left = fieldMatch.Groups.Cast<Group>().ElementAt(index: 1);
                string field = left.Success ? left.Value : GroupValue(fieldMatch, index: 2);

                if (field.StartsWith(value: "reserved", StringComparison.OrdinalIgnoreCase))
                    continue;

                lines.Add($"printf(\",\\\"offset.{name}.{field}\\\":%llu\", (unsigned long long)offsetof(struct {nativeName},{field}));");
            }
        }

        foreach (string name in new[] { "jvmtiEventCallbacks", "jvmtiHeapCallbacks" })
        {
            foreach (string field in Fields(records[name]))
            {
                if (Regex.IsMatch(tooling, "\\b" + Regex.Escape(field) + "\\s*;", RegexOptions.CultureInvariant))
                    lines.Add($"printf(\",\\\"offset.{name}.{field}\\\":%llu\", (unsigned long long)offsetof({name},{field}));");
            }
        }

        foreach (string name in Capabilities)
        {
            lines.Add($"jvmtiCapabilities {name} = {{0}}; {name}.{name} = 1;");
            lines.Add($"unsigned long long bits_{name} = 0; memcpy(&bits_{name}, &{name}, 8);");
            lines.Add($"printf(\",\\\"capability.{name}\\\":%llu\", bits_{name});");
        }

        lines.Add(item: "puts(\"}\");");
        lines.Add(item: "return 0;");
        lines.Add(item: "}");

        return string.Join(separator: '\n', lines) + "\n";
    }

    public void GenerateInspector(bool check)
    {
        List<string> entries = [];

        foreach ((string name, StructDeclarationSyntax declaration) in Records())
        {
            entries.Add($"        values[\"size.{name}\"] = sizeof({name});");

            if (TableStructures.Contains(name, StringComparer.Ordinal))
            {
                foreach (string fieldName in Fields(declaration))
                    entries.Add($"        values[\"offset.{name}.{fieldName}\"] = Marshal.OffsetOf<{name}>(nameof({name}.{fieldName})).ToInt64();");
            }
        }

        foreach (string name in Capabilities)
        {
            entries.Add($"        jvmtiCapabilities {name} = default;");
            entries.Add($"        {name}.{name} = 1;");
            entries.Add($"        values[\"capability.{name}\"] = *(long*)&{name};");
        }

        string prefix = "// <auto-generated/>\nusing System.Runtime.InteropServices;\nusing System.Text.Json;\nusing JvmBridge.Native;\ninternal static unsafe class AbiInspector\n{\n    public static void Write()\n    {\n        Dictionary<string, long> values = new();\n";
        string suffix = "\n        Console.WriteLine(JsonSerializer.Serialize(values, AbiJsonContext.Default.DictionaryStringInt64));\n    }\n}\n[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, long>))]\ninternal partial class AbiJsonContext : System.Text.Json.Serialization.JsonSerializerContext;\n";
        string text = prefix + string.Join(separator: '\n', entries) + suffix;
        string path = _repository.PathFromRoot(parts: ["tests", "Fixtures", "AbiInspector.g.cs"]);

        if (check)
        {
            if (!File.Exists(path) || !string.Equals(File.ReadAllText(path), text, StringComparison.Ordinal))
                throw new InvalidOperationException(message: "ABI inspector is stale. Run `dotnet msbuild build.proj -t:Generate`.");
        }
        else
        {
            TextFile.Write(path, text);
        }
    }

    public Dictionary<string, StructDeclarationSyntax> Records()
    {
        string source = File.ReadAllText(_repository.PathFromRoot(parts: ["src", "JvmBridge", "Native", "Generated", "Bindings.g.cs"]));

        return ParseRecords(source);
    }

    internal static IEnumerable<string> Fields(StructDeclarationSyntax declaration)
    {
        foreach (MemberDeclarationSyntax member in declaration.Members)
        {
            if (member is not FieldDeclarationSyntax field || field.Modifiers.Any(SyntaxKind.StaticKeyword))
                continue;

            foreach (VariableDeclaratorSyntax variable in field.Declaration.Variables)
                yield return variable.Identifier.ValueText;
        }
    }

    internal static Dictionary<string, StructDeclarationSyntax> ParseRecords(string source)
    {
        Dictionary<string, StructDeclarationSyntax> result = [];

        CompilationUnitSyntax root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();

        foreach (StructDeclarationSyntax declaration in root.DescendantNodes().OfType<StructDeclarationSyntax>())
        {
            if (declaration.Members.Count > 0)
                result.Add(declaration.Identifier.ValueText, declaration);
        }

        return result;
    }

    private static string GroupValue(Match match, int index)
    {
        return match.Groups.Cast<Group>().ElementAt(index).Value;
    }

    [GeneratedRegex("\\(JNICALL \\*(\\w+)\\)|void\\s*\\*\\s*(reserved\\d+)\\s*;", RegexOptions.CultureInvariant
    )]
    private static partial Regex MyRegex();
}
