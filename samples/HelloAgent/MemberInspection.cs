using System.Runtime.InteropServices;

using JvmBridge.Agents;
using JvmBridge.Native;
using JvmBridge.Runtime;


using static HelloAgent.BridgeAgent;

namespace HelloAgent;

/// <summary>The native callbacks with which the fixture has the agent inspect the members of a class through JNI: fields, methods, arrays, strings and the exceptions they raise.</summary>
internal static unsafe class MemberInspection
{
    [UnmanagedCallersOnly]
    internal static void InspectMembers(JNINativeInterface_** nativeEnvironment, _jobject* caller, _jobject* type)
    {
        try
        {
            if ((*nativeEnvironment)->ExceptionCheck(nativeEnvironment) != 0)
                return;

            AgentContext context = BridgeAgent.Context ?? throw new InvalidOperationException(message: "Agent is not active.");

            using JavaEnvironment environment = new((nint)nativeEnvironment);

            nint owner = (nint)type;

            using JavaLocalReference parent = environment.GetSuperclass(owner);

            Require(parent.Handle != 0 && context.GetClassSignature(parent.Handle).Contains(value: "MemberBase", StringComparison.Ordinal),
                message: "Superclass lookup failed");

            using JavaLocalReference root = environment.GetSuperclass(parent.Handle);

            using JavaLocalReference bootstrap = context.GetClassLoader(environment, root.Handle);

            Require(bootstrap.Handle == 0 && context.GetClassModifiers(owner) != 0 && context.IsModifiableClass(owner),
                message: "Class inspection failed");

            using JavaLocalReference loader = context.GetClassLoader(environment, owner);

            Require(loader.Handle != 0, message: "Fixture loader was null");

            using JavaLocalReference objectClass = environment.GetObjectClass(loader.Handle);

            Require(environment.IsInstanceOf(loader.Handle, objectClass.Handle), message: "Object-class query failed");

            using JavaLocalReference noParent = environment.GetSuperclass(root.Handle);

            Require(noParent.Handle == 0, message: "Root superclass was not null");

            for (int repeat = 0; repeat < 100; repeat++)
            {
                JavaFieldInfo[] fields = context.GetDeclaredFields(owner);
                JavaMethodInfo[] methods = context.GetDeclaredMethods(owner);

                Require(fields.Single(field => field.Name == "λ").Descriptor == "I", message: "Unicode field metadata failed");

                Require(fields.Single(field => field.Name == "generic").GenericSignature is not null,
                    message: "Generic field signature missing");

                Require(fields.Single(field => field.Name == "profile").GenericSignature is null,
                    message: "Non-generic field signature was not null");

                Require(methods.Single(method => method.Name == "genericMethod").GenericSignature is not null,
                    message: "Generic method signature missing");

                Require(methods.Single(method => method.Name == "longValue").Descriptor == "()J",
                    message: "Method descriptor failed");

                Require((context.GetDeclaredFields(parent.Handle).Single(field => field.Name == "inherited").Modifiers & 0x0002) != 0,
                    message: "Private parent field missing");
            }

            nint constructor = environment.GetMethod(owner, name: "<init>", signature: "(I)V");

            using JavaLocalReference created = environment.NewObject(owner,
                constructor,
                arguments: [new jvalue
                {
                    i = 41
                }]);

            Require(environment.GetIntField(created.Handle, environment.GetField(owner, name: "count", descriptor: "I")) == 41,
                message: "Constructor did not initialize instance");

            nint inherited = environment.GetField(parent.Handle, name: "inherited", descriptor: "I");

            Require(environment.GetIntField(created.Handle, inherited) == 19, message: "Inherited private field missing");

            nint profile = environment.GetField(owner, name: "profile", descriptor: "Ljava/lang/Object;");

            using (JavaLocalReference missing = environment.GetObjectField(created.Handle, profile))
                Require(missing.Handle == 0, message: "Null field was not empty");

            using JavaLocalReference text = environment.NewString(value: "value");

            environment.SetObjectField(created.Handle, profile, text.Handle);

            using (JavaLocalReference value = environment.GetObjectField(created.Handle, profile))
                Require(environment.GetString(value.Handle) == "value", message: "Object field round trip failed");

            environment.SetObjectField(created.Handle, profile, value: 0);
            environment.SetBooleanField(created.Handle, environment.GetField(owner, name: "flag", descriptor: "Z"), value: true);

            Require(environment.GetBooleanField(created.Handle, environment.GetField(owner, name: "flag", descriptor: "Z")),
                message: "Boolean field failed");

            environment.SetByteField(created.Handle, environment.GetField(owner, name: "tiny", descriptor: "B"), value: -8);

            Require(environment.GetByteField(created.Handle, environment.GetField(owner, name: "tiny", descriptor: "B")) == -8,
                message: "Byte field failed");

            environment.SetCharField(created.Handle,
                environment.GetField(owner, name: "letter", descriptor: "C"),
                value: '\uD800');

            Require(environment.GetCharField(created.Handle, environment.GetField(owner, name: "letter", descriptor: "C")) == '\uD800',
                message: "Char field failed");

            environment.SetShortField(created.Handle, environment.GetField(owner, name: "small", descriptor: "S"), value: -100);

            Require(environment.GetShortField(created.Handle, environment.GetField(owner, name: "small", descriptor: "S")) == -100,
                message: "Short field failed");

            environment.SetIntField(created.Handle, inherited, value: 27);
            Require(environment.GetIntField(created.Handle, inherited) == 27, message: "Inherited field write failed");

            environment.SetLongField(created.Handle,
                environment.GetField(owner, name: "large", descriptor: "J"),
                value: 1234567890123L);

            Require(environment.GetLongField(created.Handle, environment.GetField(owner, name: "large", descriptor: "J")) == 1234567890123L,
                message: "Long field failed");

            environment.SetFloatField(created.Handle,
                environment.GetField(owner, name: "fraction", descriptor: "F"),
                value: 1.25f);

            Require(environment.GetFloatField(created.Handle, environment.GetField(owner, name: "fraction", descriptor: "F")) == 1.25f,
                message: "Float field failed");

            environment.SetDoubleField(created.Handle,
                environment.GetField(owner, name: "precision", descriptor: "D"),
                value: -2.5);

            Require(environment.GetDoubleField(created.Handle, environment.GetField(owner, name: "precision", descriptor: "D")) == -2.5,
                message: "Double field failed");

            nint shared = environment.GetField(owner, name: "shared", descriptor: "Ljava/lang/Object;", isStatic: true);

            using (JavaLocalReference empty = environment.GetObjectField(owner, shared, isStatic: true))
                Require(empty.Handle == 0, message: "Static null field failed");

            environment.SetObjectField(owner, shared, text.Handle, isStatic: true);

            using (JavaLocalReference set = environment.GetObjectField(owner, shared, isStatic: true))
                Require(environment.GetString(set.Handle) == "value", message: "Static object field failed");

            environment.SetObjectField(owner, shared, value: 0, isStatic: true);

            nint staticLarge = environment.GetField(owner, name: "staticLarge", descriptor: "J", isStatic: true);

            environment.SetLongField(owner, staticLarge, value: 123L, isStatic: true);

            Require(environment.GetLongField(owner, staticLarge, isStatic: true) == 123L,
                message: "Static primitive field failed");

            Require(environment.CallBoolean(created.Handle,
                    environment.GetMethod(owner, name: "isReady", signature: "()Z"),
                    arguments: []),
                message: "Boolean call failed");

            Require(environment.CallByte(created.Handle,
                    environment.GetMethod(owner, name: "byteValue", signature: "()B"),
                    arguments: []) == -7,
                message: "Byte call failed");

            Require(environment.CallChar(created.Handle,
                    environment.GetMethod(owner, name: "charValue", signature: "()C"),
                    arguments: []) == '\uD800',
                message: "Char call failed");

            Require(environment.CallShort(created.Handle,
                    environment.GetMethod(owner, name: "shortValue", signature: "()S"),
                    arguments: []) == -11,
                message: "Short call failed");

            Require(environment.CallLong(created.Handle,
                    environment.GetMethod(owner, name: "longValue", signature: "()J"),
                    arguments: []) == 1234567890123L,
                message: "Long call failed");

            Require(environment.CallFloat(created.Handle,
                    environment.GetMethod(owner, name: "floatValue", signature: "()F"),
                    arguments: []) == 1.25f,
                message: "Float call failed");

            Require(environment.CallDouble(created.Handle,
                    environment.GetMethod(owner, name: "doubleValue", signature: "()D"),
                    arguments: []) == -2.5,
                message: "Double call failed");

            Require(environment.CallBoolean(owner,
                    environment.GetMethod(owner, name: "staticReady", signature: "()Z", isStatic: true),
                    arguments: [],
                    isStatic: true),
                message: "Static boolean call failed");

            Require(environment.CallLong(owner,
                    environment.GetMethod(owner, name: "staticValue", signature: "()J", isStatic: true),
                    arguments: [],
                    isStatic: true) == 100L,
                message: "Static long call failed");

            nint repeatMethod = environment.GetMethod(owner, name: "longValue", signature: "()J");

            for (int repeat = 0; repeat < 100; repeat++)
            {
                using JavaLocalReference empty = environment.GetObjectField(created.Handle, profile);

                Require(empty.Handle == 0 && environment.CallLong(created.Handle, repeatMethod, arguments: []) == 1234567890123L,
                    message: "Repeated calls failed");
            }

            ExpectJavaException(() => Require(environment.GetField(owner, name: "absent", descriptor: "I") == 0,
                message: "Field found"));

            ExpectJavaException(() => Require(environment.GetMethod(owner, name: "absent", signature: "()V") == 0,
                message: "Method found"));

            ExpectJavaException(() => environment.CallVoid(created.Handle,
                environment.GetMethod(owner, name: "fail", signature: "()V"),
                arguments: []));

            ExpectJavaException(() =>
            {
                using JavaLocalReference ignored = environment.NewObject(owner,
                    constructor,
                    arguments: [new jvalue
                    {
                        i = -1
                    }]);
            });

            JavaException detailed = CaptureJavaException(() => environment.CallVoid(created.Handle,
                environment.GetMethod(owner, name: "failDetailed", signature: "()V"),
                arguments: []));

            Require(detailed.Operation == nameof(JavaEnvironment.CallVoid), message: "Java call operation was lost");

            Require(detailed.JavaTypeName == "java.lang.IllegalStateException" && detailed.JavaMessage == "é 😀",
                message: "Unicode Java diagnostics were lost");

            Require(detailed.JavaStackTrace?.Contains(value: "BridgeFixture$MemberChild.failDetailed(BridgeFixture.java:", StringComparison.Ordinal) == true,
                message: "Java stack location was lost");

            Require(detailed.JavaStackTrace?.Contains(value: "Caused by: java.lang.IllegalArgumentException: cause Ω", StringComparison.Ordinal) == true,
                message: "Java cause was lost");

            JavaException lookup = CaptureJavaException(() => Require(environment.GetMethod(owner,
                    name: "absentDiagnostic",
                    signature: "()V") == 0,
                message: "Found"));

            Require(lookup.Operation == nameof(JavaEnvironment.GetMethod) && lookup.JavaTypeName == "java.lang.NoSuchMethodError",
                message: "Java lookup diagnostics were lost");

            JavaException construction = CaptureJavaException(() =>
            {
                using JavaLocalReference ignored = environment.NewObject(owner,
                    constructor,
                    arguments: [new jvalue
                    {
                        i = -1
                    }]);
            });

            Require(construction.Operation == nameof(JavaEnvironment.NewObject) && construction.JavaTypeName == "java.lang.IllegalArgumentException" && construction.JavaMessage == "negative",
                message: "Java construction diagnostics were lost");

            nint brokenDiagnostics = environment.GetMethod(owner, name: "failDiagnostic", signature: "()V");

            for (int repeat = 0; repeat < 100; repeat++)
            {
                JavaException failure = CaptureJavaException(() => environment.CallVoid(created.Handle,
                    brokenDiagnostics,
                    arguments: []));

                Require(failure.Operation == nameof(JavaEnvironment.CallVoid) && failure.JavaTypeName == "BridgeFixture$DiagnosticFailure",
                    message: "Failed diagnostic method masked the original throwable");

                Require(failure.JavaMessage is null && failure.JavaStackTrace is not null,
                    message: "Failed diagnostic method corrupted the snapshot");

                Require(environment.CallLong(created.Handle, repeatMethod, arguments: []) == 1234567890123L,
                    message: "JNI did not recover after diagnostic failure");
            }

            Log(message: "MEMBER_INSPECTION_OK");
        }
        catch (Exception exception) when (ContainLoggingFailure(exception))
        {
            Log(message: $"NATIVE_ERROR {exception.Message}");
        }
    }

    [UnmanagedCallersOnly]
    internal static void InspectTwinTypes(JNINativeInterface_** nativeEnvironment, _jobject* caller, _jobject* first, _jobject* second)
    {
        try
        {
            if ((*nativeEnvironment)->ExceptionCheck(nativeEnvironment) != 0)
                return;

            AgentContext context = BridgeAgent.Context ?? throw new InvalidOperationException(message: "Agent is not active.");

            using JavaEnvironment environment = new((nint)nativeEnvironment);

            Require(context.GetClassSignature((nint)first) == context.GetClassSignature((nint)second),
                message: "Twin signatures differed");

            using JavaLocalReference firstLoader = context.GetClassLoader(environment, (nint)first);

            using JavaLocalReference secondLoader = context.GetClassLoader(environment, (nint)second);

            Require(!environment.IsSameObject(firstLoader.Handle, secondLoader.Handle), message: "Twin loaders were identical");

            JavaFieldInfo firstField = context.GetDeclaredFields((nint)first).Single(static field => field.Name == "marker");
            JavaFieldInfo secondField = context.GetDeclaredFields((nint)second).Single(static field => field.Name == "marker");

            Require(firstField.Descriptor == secondField.Descriptor && firstField.Id != secondField.Id,
                message: "Field identifiers were not loader-specific");

            JavaMethodInfo firstMethod = context.GetDeclaredMethods((nint)first)
                .Single(static method => method.Name == "message");

            JavaMethodInfo secondMethod = context.GetDeclaredMethods((nint)second)
                .Single(static method => method.Name == "message");

            Require(firstMethod.Descriptor == secondMethod.Descriptor && firstMethod.Id != secondMethod.Id,
                message: "Method identifiers were not loader-specific");

            using JavaLocalReference marker = environment.NewString(value: "selected");

            environment.SetObjectField((nint)first, firstField.Id, marker.Handle, isStatic: true);

            using JavaLocalReference selected = environment.GetObjectField((nint)first, firstField.Id, isStatic: true);

            using JavaLocalReference other = environment.GetObjectField((nint)second, secondField.Id, isStatic: true);

            Require(environment.GetString(selected.Handle) == "selected" && environment.GetString(other.Handle) == "twin",
                message: "Twin fields crossed loaders");

            Log(message: "LOADER_METADATA_OK");
        }
        catch (Exception exception) when (ContainLoggingFailure(exception))
        {
            Log(message: $"NATIVE_ERROR {exception.Message}");
        }
    }
}
