using System.Globalization;
using System.IO.Compression;

using JvmBridge.Agents;
using JvmBridge.ClassFiles;

namespace JvmBridge.Testing;

/// <summary>
/// Resolves the classes, fields and methods a helper refers to the way the JVM links them: in the classes of a class path, with the patches applied that the agent registers, walking superclasses and interfaces, and in the JDK. The application's classes are
/// read from their class files, so their own dependencies need not be there; the JDK's are looked up by a small Java program on the JDK under test.
/// </summary>
internal static class HelperLinks
{
    private const string CheckerSource = """
        import java.io.BufferedReader;
        import java.io.FileInputStream;
        import java.io.InputStreamReader;
        import java.lang.reflect.Constructor;
        import java.lang.reflect.Field;
        import java.lang.reflect.Method;

        /** Answers, for each line "C|F|M owner [name descriptor]" of a file, whether the JDK has the class and the class or a supertype declares the member. */
        public final class JvmBridgeLinkCheck {
            private JvmBridgeLinkCheck() {
            }

            public static void main(String[] arguments) throws Exception {
                BufferedReader reader = new BufferedReader(new InputStreamReader(new FileInputStream(arguments[0]), "UTF-8"));
                try {
                    String line;
                    while ((line = reader.readLine()) != null) {
                        String[] parts = line.split(" ");
                        Class<?> type;
                        try {
                            type = Class.forName(parts[1].replace('/', '.'), false, JvmBridgeLinkCheck.class.getClassLoader());
                        } catch (Throwable missing) {
                            System.out.println("LINK NOCLASS");
                            continue;
                        }
                        boolean found = parts[0].equals("C") || (parts[0].equals("F") ? hasField(type, parts[2], parts[3]) : hasMethod(type, parts[2], parts[3]));
                        System.out.println(found ? "LINK FOUND" : "LINK MISSING");
                    }
                } finally {
                    reader.close();
                }
            }

            private static boolean hasField(Class<?> type, String name, String descriptor) {
                for (Field field : type.getDeclaredFields()) {
                    if (field.getName().equals(name) && descriptor(field.getType()).equals(descriptor)) {
                        return true;
                    }
                }
                for (Class<?> parent : type.getInterfaces()) {
                    if (hasField(parent, name, descriptor)) {
                        return true;
                    }
                }
                return type.getSuperclass() != null && hasField(type.getSuperclass(), name, descriptor);
            }

            private static boolean hasMethod(Class<?> type, String name, String descriptor) {
                if (name.equals("<init>")) {
                    for (Constructor<?> constructor : type.getDeclaredConstructors()) {
                        if (descriptor(constructor.getParameterTypes(), void.class).equals(descriptor)) {
                            return true;
                        }
                    }
                    return false;
                }
                for (Method method : type.getDeclaredMethods()) {
                    if (method.getName().equals(name) && descriptor(method.getParameterTypes(), method.getReturnType()).equals(descriptor)) {
                        return true;
                    }
                }
                for (Class<?> parent : type.getInterfaces()) {
                    if (hasMethod(parent, name, descriptor)) {
                        return true;
                    }
                }
                return type.getSuperclass() != null && hasMethod(type.getSuperclass(), name, descriptor);
            }

            private static String descriptor(Class<?>[] parameters, Class<?> result) {
                StringBuilder text = new StringBuilder("(");
                for (Class<?> parameter : parameters) {
                    text.append(descriptor(parameter));
                }
                return text.append(')').append(descriptor(result)).toString();
            }

            private static String descriptor(Class<?> type) {
                if (type.isArray()) {
                    return type.getName().replace('.', '/');
                }
                if (!type.isPrimitive()) {
                    return "L" + type.getName().replace('.', '/') + ";";
                }
                String[] names = { "boolean", "byte", "char", "short", "int", "long", "float", "double", "void" };
                String letters = "ZBCSIJFDV";
                for (int index = 0; index < names.length; index++) {
                    if (names[index].equals(type.getName())) {
                        return letters.substring(index, index + 1);
                    }
                }
                throw new IllegalArgumentException(type.getName());
            }
        }
        """;
    private const int PrivateFlag = 0x0002;

    private const int PublicFlag = 0x0001;

    /// <summary>Lists what a helper refers to and cannot link to, one line each; empty when everything resolves and is accessible.</summary>
    internal static async Task<IReadOnlyList<string>> FindProblemsAsync(
        Jdk jdk,
        JavaClassDefinition helper,
        IEnumerable<string> classPath,
        IEnumerable<ClassPatch> patches,
        CancellationToken cancellationToken
    )
    {
        Dictionary<string, JavaClassFile> application = ReadClassPath(classPath, [.. patches]);
        List<JavaClassFile> helpers = [.. Flatten(helper).Select(static definition => JavaClassFile.Read(definition.Bytes.Span))];
        HashSet<string> own = [.. helpers.Select(static file => file.Name)];
        List<string> problems = [];

        // What only the JDK can answer: each lookup asks about one or more classes, and fails unless one of them has the class or member.
        List<Lookup> lookups = [];
        List<Question> questions = [];

        foreach (MemberUse use in helpers.SelectMany(static file => MemberReferences(file).Select(reference => new MemberUse(file.Name, reference))).Distinct())
        {
            MemberReference reference = use.Reference;

            if (own.Contains(reference.Owner) || reference.Owner.StartsWith(value: '['))
                continue;

            bool isField = reference.Kind == ConstantTag.Fieldref;
            string member = $"{reference.Owner}.{reference.Name}{(isField ? ":" : string.Empty)}{reference.Descriptor}";
            Resolution resolution = Resolve(application, reference, isField);

            if (resolution.Member is { } declared && resolution.Declarer is { } declarer)
            {
                if (Inaccessible(declared, declarer, use.From) is { } reason)
                    problems.Add($"{use.From} uses {member}, which is {reason}");
            }
            else if (resolution.Outside.Count == 0)
            {
                problems.Add($"{use.From} uses {member}, which {reference.Owner} and its supertypes on the class path do not declare");
            }
            else
            {
                // The member may come from the JDK: from the class itself, or from a JDK class that an application class extends.
                string missing = $"{use.From} uses {member}, which neither the class path nor the JDK declares";
                string noClass = resolution.Outside.Contains(reference.Owner) ? $"{use.From} uses {member}, but {reference.Owner} is neither on the class path nor in the JDK" : missing;
                string kind = isField ? "F" : "M";

                lookups.Add(new Lookup(missing, noClass));
                questions.AddRange(resolution.Outside.Select(type => new Question($"{kind} {type} {reference.Name} {reference.Descriptor}", lookups.Count - 1)));
            }
        }

        // A class that a member reference names is checked with the member, so only the other classes are looked up on their own.
        HashSet<string> owners = [.. helpers.SelectMany(MemberReferences).Select(static reference => reference.Owner)];

        foreach (ClassUse use in helpers.SelectMany(static file => ClassReferences(file).Select(type => new ClassUse(file.Name, type))).Distinct())
        {
            if (own.Contains(use.Type) || owners.Contains(use.Type))
                continue;

            if (!application.TryGetValue(use.Type, out JavaClassFile? file))
            {
                string absent = $"{use.From} uses the class {use.Type}, which is neither on the class path nor in the JDK";

                lookups.Add(new Lookup(absent, absent));
                questions.Add(new Question($"C {use.Type}", lookups.Count - 1));
            }
            else if ((file.AccessFlags & PublicFlag) == 0 && Package(use.Type) != Package(use.From))
            {
                problems.Add($"{use.From} uses the class {use.Type}, which is not public and in another package");
            }
        }

        if (questions.Count == 0)
            return problems;

        string[] answers = await AskJdkAsync(jdk, [.. questions.Select(static question => question.Query)], cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        for (int index = 0; index < lookups.Count; index++)
        {
            List<string> mine = [];

            for (int position = 0; position < questions.Count; position++)
            {
                if (questions[position].Lookup == index)
                    mine.Add(answers[position]);
            }

            if (!mine.Contains(item: "FOUND"))
                problems.Add(mine.TrueForAll(static answer => answer == "NOCLASS") ? lookups[index].NoClass : lookups[index].Missing);
        }

        return problems;
    }

    private static void Add(Dictionary<string, byte[]> classes, byte[] bytes)
    {
        JavaClassFile file = JavaClassFile.Read(bytes);

        if (file.Name != "module-info" && !classes.ContainsKey(file.Name))
            classes.Add(file.Name, bytes);
    }

    private static async Task<string[]> AskJdkAsync(Jdk jdk, string[] queries, CancellationToken cancellationToken)
    {
        string work = Directory.CreateTempSubdirectory(prefix: "jvmbridge-links-").FullName;

        try
        {
            string source = Path.Combine(work, path2: "JvmBridgeLinkCheck.java");
            string questions = Path.Combine(work, path2: "questions.txt");
            await File.WriteAllTextAsync(source, CheckerSource, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            await File.WriteAllLinesAsync(questions, queries, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            string classes = await jdk.CompileAsync(source, Math.Min(jdk.Major, val2: 8), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            try
            {
                JavaRunOptions options = new() { PreloadSignalChaining = false };
                options.Arguments.Add(questions);

                JavaRun run = await jdk.RunAsync(mainClass: "JvmBridgeLinkCheck", classes, options: options, cancellationToken: cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
                string[] answers = [.. run.Output.Split(separator: '\n').Select(static line => line.Trim()).Where(static line => line.StartsWith(value: "LINK ", StringComparison.Ordinal)).Select(static line => line[5..])];

                return run.ExitCode == 0 && answers.Length == queries.Length
                    ? answers
                    : throw new InvalidOperationException(
                        $"The link check on {jdk} answered {answers.Length.ToString(CultureInfo.InvariantCulture)} of {queries.Length.ToString(CultureInfo.InvariantCulture)} questions:" + Environment.NewLine + run.Output
                    );
            }
            finally
            {
                Directory.Delete(classes, recursive: true);
            }
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    private static IEnumerable<string> ClassReferences(JavaClassFile file)
    {
        foreach (ConstantPoolEntry? entry in file.ConstantPool)
        {
            if (entry is not ClassEntry classEntry)
                continue;

            string name = file.GetUtf8(classEntry.NameIndex);
            string element = name.TrimStart(trimChar: '[');

            if (element.Length == name.Length)
                yield return name;
            else if (element.StartsWith(value: 'L') && element.EndsWith(value: ';'))
                yield return element[1..^1];
        }
    }

    private static IEnumerable<JavaClassDefinition> Flatten(JavaClassDefinition definition)
    {
        return [definition, .. definition.NestedClasses.SelectMany(Flatten)];
    }

    private static string? Inaccessible(ClassMember member, string declarer, string from)
    {
        if ((member.AccessFlags & PrivateFlag) != 0)
            return $"private in {declarer}: open it with SetFieldAccess or SetMethodAccess, or add a getter or an invoker";

        bool samePackage = Package(declarer) == Package(from);

        return (member.AccessFlags & PublicFlag) != 0 || samePackage ? null : $"not public in {declarer}, in another package than {from}";
    }

    private static IEnumerable<MemberReference> MemberReferences(JavaClassFile file)
    {
        for (int index = 1; index < file.ConstantPool.Count; index++)
        {
            if (file.ConstantPool[index] is { Tag: ConstantTag.Fieldref or ConstantTag.Methodref or ConstantTag.InterfaceMethodref })
                yield return file.GetMemberReference(index);
        }
    }

    private static string Package(string name)
    {
        int slash = name.LastIndexOf(value: '/');

        return slash >= 0 ? name[..slash] : string.Empty;
    }

    // The application's classes from jars and class directories, the first of a name winning as on a class path, each with the patches applied that target it.
    private static Dictionary<string, JavaClassFile> ReadClassPath(IEnumerable<string> classPath, ClassPatch[] patches)
    {
        Dictionary<string, byte[]> classes = new(StringComparer.Ordinal);

        foreach (string entry in classPath)
        {
            if (Directory.Exists(entry))
            {
                foreach (string path in Directory.EnumerateFiles(entry, searchPattern: "*.class", SearchOption.AllDirectories))
                    Add(classes, File.ReadAllBytes(path));

                continue;
            }

            using ZipArchive jar = ZipFile.OpenRead(entry);

            foreach (ZipArchiveEntry file in jar.Entries.Where(
                static file => file.FullName.EndsWith(value: ".class", StringComparison.Ordinal) && !file.FullName.StartsWith(value: "META-INF/", StringComparison.Ordinal)
            ))
            {
                using Stream stream = file.Open();

                using MemoryStream copy = new();

                stream.CopyTo(copy);
                Add(classes, copy.ToArray());
            }
        }

        ClassHierarchy hierarchy = new();

        foreach (JavaClassFile file in classes.Values.Select(static bytes => JavaClassFile.Read(bytes)))
            hierarchy.Add(file.Name, file.SuperName, file.Interfaces);

        Dictionary<string, JavaClassFile> patched = new(StringComparer.Ordinal);

        foreach (KeyValuePair<string, byte[]> original in classes)
        {
            byte[] bytes = original.Value;

            foreach (ClassPatch patch in patches.Where(patch => patch.Matches(original.Value, hierarchy)))
                bytes = patch.Apply(bytes).Bytes ?? bytes;

            patched[original.Key] = JavaClassFile.Read(bytes);
        }

        return patched;
    }

    // Walks the class and its supertypes on the class path; a supertype that is not there is a JDK class, which the JDK is asked about.
    private static Resolution Resolve(Dictionary<string, JavaClassFile> application, MemberReference reference, bool isField)
    {
        Queue<string> pending = new([reference.Owner]);
        HashSet<string> seen = new(StringComparer.Ordinal);
        List<string> outside = [];

        while (pending.TryDequeue(out string? type))
        {
            if (!seen.Add(type))
                continue;

            if (!application.TryGetValue(type, out JavaClassFile? file))
            {
                outside.Add(type);

                continue;
            }

            ClassMember? member = isField
                ? file.Fields.FirstOrDefault(field => field.Name == reference.Name && field.Descriptor == reference.Descriptor)
                : file.FindMethod(reference.Name, reference.Descriptor);

            if (member is not null)
                return new Resolution(member, type, outside);

            // A constructor belongs to its class alone.
            if (reference.Name == "<init>")
                break;

            if (file.SuperName is { } superName)
                pending.Enqueue(superName);

            foreach (string implemented in file.Interfaces)
                pending.Enqueue(implemented);
        }

        return new Resolution(Member: null, Declarer: null, outside);
    }

    private sealed record ClassUse(string From, string Type);

    // The two ways a lookup can fail: the classes exist without the member, or none of them exists.
    private sealed record Lookup(string Missing, string NoClass);

    private sealed record MemberUse(string From, MemberReference Reference);

    private sealed record Question(string Query, int Lookup);

    private sealed record Resolution(ClassMember? Member, string? Declarer, List<string> Outside);
}
