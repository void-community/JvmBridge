using System.Runtime.InteropServices;
using System.Text;

using JvmBridge.Agents;
using JvmBridge.Native;
using JvmBridge.Runtime;

namespace HelloAgent;

/// <summary>Demonstrates native registration, transformations, and thread-owned JNI references.</summary>
internal sealed unsafe class BridgeAgent : JavaAgent
{
    private static BridgeAgent? s_agent;
    private static AgentContext? s_context;
    private readonly List<(JavaGlobalReference Loader, JavaGlobalReference Class)> _helpers = [];
    private JavaWeakGlobalReference? _observedWeak;
    private JavaGlobalReference? _targetLoader;

    /// <inheritdoc/>
    public override void Configure(AgentContext context)
    {
        if (!context.TryEnableRetransformation())
            Log(message: "RETRANSFORM_UNAVAILABLE");
    }

    /// <inheritdoc/>
    public override void OnAttach(AgentContext context)
    {
        OnLoad(context);
        Log(message: "ATTACH");

        using JavaThreadAttachment attachment = context.VirtualMachine.AttachCurrentThread();

        JavaEnvironment environment = attachment.Environment;
        InstallSystemHelper(environment);
        int localCapacity = 4096;
        int capacityResult = environment.Functions->EnsureLocalCapacity((JNINativeInterface_**)environment.Handle, localCapacity);
        environment.ThrowIfException(operation: "EnsureLocalCapacity");

        if (capacityResult != 0)
            throw new JniException(capacityResult, operation: "EnsureLocalCapacity");

        nint[] classes = context.GetLoadedClasses(environment);

        try
        {
            foreach (nint type in classes)
            {
                if (context.GetClassSignature(type) == "LBridgeFixture;")
                {
                    Register(environment, type);

                    if (context.CanRetransform)
                        context.Retransform(type);
                }
            }
        }
        finally
        {
            foreach (nint type in classes)
                environment.Functions->DeleteLocalRef((JNINativeInterface_**)environment.Handle, (_jobject*)type);
        }
    }

    /// <inheritdoc/>
    public override void OnClassPrepare(AgentContext context, JavaEnvironment environment, nint type)
    {
        if (context.GetClassSignature(type) == "LBridgeFixture;")
            Register(environment, type);
    }

    /// <inheritdoc/>
    public override void OnLoad(AgentContext context)
    {
        s_context = context;
        s_agent = this;

        if (context.Options.Contains(value: "fail-start", StringComparison.Ordinal))
            throw new InvalidOperationException(message: "Intentional initialization failure.");

        Log($"LOAD retransform={context.CanRetransform}");
    }

    /// <inheritdoc/>
    public override void OnUnload(AgentContext context)
    {
        _observedWeak?.Dispose();
        _observedWeak = null;
        _targetLoader?.Dispose();
        _targetLoader = null;

        foreach ((JavaGlobalReference loader, JavaGlobalReference type) in _helpers)
        {
            type.Dispose();
            loader.Dispose();
        }

        _helpers.Clear();
        Log(message: "UNLOAD");
    }

    /// <inheritdoc/>
    public override void OnVmDeath(AgentContext context, JavaEnvironment environment)
    {
        Log(message: "VM_DEATH");
    }

    /// <inheritdoc/>
    public override void OnVmInit(AgentContext context, JavaEnvironment environment)
    {
        Log(message: "VM_INIT");
        InstallSystemHelper(environment);

        using JavaLocalReference system = environment.FindClass(name: "java/lang/System");

        nint method = environment.GetMethod(system.Handle, name: "setProperty", signature: "(Ljava/lang/String;Ljava/lang/String;)Ljava/lang/String;", isStatic: true);

        using JavaLocalReference name = environment.NewString(value: "jvmbridge.loaded");

        using JavaLocalReference value = environment.NewString(value: "true");

        jvalue[] arguments = [new() { l = (_jobject*)name.Handle }, new() { l = (_jobject*)value.Handle }];

        using JavaLocalReference previous = environment.CallObject(system.Handle, method, arguments, isStatic: true);

        if (context.Options.Contains(value: "fail-callback", StringComparison.Ordinal))
            throw new InvalidOperationException(message: "Intentional callback failure.");
    }

    /// <inheritdoc/>
    public override byte[]? TransformClass(AgentContext context, ClassFile file)
    {
        if (file.Name == CallbackClassFile.Name)
            return null;

        if (file.Name != "BridgeFixture")
            return null;

        // The fixture's unique constant has equal-length replacements. This is
        // intentionally an example, not a general Java bytecode rewriting API.
        byte[] result = file.Bytes.ToArray();
        byte[] original = Encoding.ASCII.GetBytes(s: "ORIGINAL");
        byte[] replacement = Encoding.ASCII.GetBytes(file.IsRetransformation ? "RELOADED" : "MODIFIED");
        int index = result.AsSpan().IndexOf(original);

        if (index < 0)
            throw new InvalidOperationException(message: "Fixture constant was not found.");

        replacement.CopyTo(result, index);

        if (_helpers.Count > 0)
            result = CallbackClassFile.Inject(result);

        Log(file.IsRetransformation ? "RETRANSFORM" : "TRANSFORM");

        return result;
    }

    /// <inheritdoc/>
    public override byte[]? TransformClass(AgentContext context, ClassFile file, ClassTransformContext callback)
    {
        if (file.Name == "java/util/concurrent/atomic/AtomicStampedReference" && callback.Loader == 0)
            Log(message: "BOOTSTRAP_CALLBACK_NULL_LOADER");

        if (file.Name != "BridgeFixture$Twin")
            return base.TransformClass(context, file, callback);

        if (!callback.CanUseJni || callback.Environment is not { } environment || _targetLoader is not { } targetLoader)
            throw new InvalidOperationException(message: "Twin transformation requires a live JNI environment and a selected loader.");

        if (callback.Loader == 0 || callback.ProtectionDomain == 0 || callback.ClassBeingRedefined != 0 != file.IsRetransformation)
            throw new InvalidOperationException(message: "Twin transformation received incomplete class metadata.");

        bool selected = environment.IsSameObject(callback.Loader, targetLoader.Handle);
        string expectedLocation = selected ? "file:/jvmbridge-selected.jar" : "file:/jvmbridge-other.jar";

        using JavaLocalReference domainType = environment.FindClass(name: "java/security/ProtectionDomain");

        nint getCodeSource = environment.GetMethod(domainType.Handle, name: "getCodeSource", signature: "()Ljava/security/CodeSource;");

        using JavaLocalReference codeSource = environment.CallObject(callback.ProtectionDomain, getCodeSource, []);

        if (codeSource.Handle == 0)
            throw new InvalidOperationException(message: "Twin protection domain has no code source.");

        using JavaLocalReference sourceType = environment.FindClass(name: "java/security/CodeSource");

        nint getLocation = environment.GetMethod(sourceType.Handle, name: "getLocation", signature: "()Ljava/net/URL;");

        using JavaLocalReference location = environment.CallObject(codeSource.Handle, getLocation, []);

        if (location.Handle == 0)
            throw new InvalidOperationException(message: "Twin code source has no location.");

        using JavaLocalReference urlType = environment.FindClass(name: "java/net/URL");

        nint toExternalForm = environment.GetMethod(urlType.Handle, name: "toExternalForm", signature: "()Ljava/lang/String;");

        using JavaLocalReference locationText = environment.CallObject(location.Handle, toExternalForm, []);

        if (environment.GetString(locationText.Handle) != expectedLocation)
            throw new InvalidOperationException(message: "Twin code source does not match its defining loader.");

        Log(selected ? "SELECTED_CODE_SOURCE" : "OTHER_CODE_SOURCE");

        if (!selected)
            return null;

        byte[] result = file.Bytes.ToArray();
        int index = result.AsSpan().IndexOf(Encoding.ASCII.GetBytes(s: "ORIGINAL"));

        if (index < 0)
            throw new InvalidOperationException(message: "Twin class constant was not found.");

        Encoding.ASCII.GetBytes(file.IsRetransformation ? "RELOADED" : "MODIFIED").CopyTo(result, index);
        result = CallbackClassFile.Inject(result);
        Log(file.IsRetransformation ? "SELECTED_RETRANSFORM" : "SELECTED_TRANSFORM");

        return result;
    }

    private static JavaException CaptureJavaException(Action action)
    {
        try { action(); }
        catch (JavaException exception) { return exception; }

        throw new InvalidOperationException(message: "Expected a cleared Java exception.");
    }

    [UnmanagedCallersOnly]
    private static void ClearTargetLoader(JNINativeInterface_** nativeEnvironment, _jobject* caller)
    {
        try
        {
            s_agent?._targetLoader?.Dispose();

            if (s_agent is { } agent)
                agent._targetLoader = null;
        }
        catch (Exception exception) when (ContainLoggingFailure(exception)) { Log($"NATIVE_ERROR {exception.Message}"); }
    }

    private static bool ContainLoggingFailure(Exception exception)
    {
        GC.KeepAlive(exception);

        return true;
    }

    private static void ExpectJavaException(Action action)
    {
        try { action(); }
        catch (JavaException) { return; }

        throw new InvalidOperationException(message: "Expected a cleared Java exception.");
    }

    [UnmanagedCallersOnly]
    private static void InspectMembers(JNINativeInterface_** nativeEnvironment, _jobject* caller, _jobject* type)
    {
        try
        {
            if ((*nativeEnvironment)->ExceptionCheck(nativeEnvironment) != 0)
                return;

            AgentContext context = s_context ?? throw new InvalidOperationException(message: "Agent is not active.");

            using JavaEnvironment environment = new((nint)nativeEnvironment);

            nint owner = (nint)type;

            using JavaLocalReference parent = environment.GetSuperclass(owner);

            Require(
                parent.Handle != 0 && context.GetClassSignature(parent.Handle).Contains(value: "MemberBase", StringComparison.Ordinal),
                message: "Superclass lookup failed"
            );

            using JavaLocalReference root = environment.GetSuperclass(parent.Handle);

            using JavaLocalReference bootstrap = context.GetClassLoader(environment, root.Handle);

            Require(
                bootstrap.Handle == 0 && context.GetClassModifiers(owner) != 0 && context.IsModifiableClass(owner),
                message: "Class inspection failed"
            );

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
                Require(fields.Single(field => field.Name == "generic").GenericSignature is not null, message: "Generic field signature missing");
                Require(fields.Single(field => field.Name == "profile").GenericSignature is null, message: "Non-generic field signature was not null");
                Require(
                    methods.Single(method => method.Name == "genericMethod").GenericSignature is not null,
                    message: "Generic method signature missing"
                );
                Require(methods.Single(method => method.Name == "longValue").Descriptor == "()J", message: "Method descriptor failed");
                Require(
                    (context.GetDeclaredFields(parent.Handle).Single(field => field.Name == "inherited").Modifiers & 0x0002) != 0,
                    message: "Private parent field missing"
                );
            }

            nint constructor = environment.GetMethod(owner, name: "<init>", signature: "(I)V");

            using JavaLocalReference created = environment.NewObject(owner, constructor, [new jvalue { i = 41 }]);

            Require(
                environment.GetIntField(created.Handle, environment.GetField(owner, name: "count", descriptor: "I")) == 41,
                message: "Constructor did not initialize instance"
            );
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
            Require(
                environment.GetBooleanField(created.Handle, environment.GetField(owner, name: "flag", descriptor: "Z")),
                message: "Boolean field failed"
            );
            environment.SetByteField(created.Handle, environment.GetField(owner, name: "tiny", descriptor: "B"), value: -8);
            Require(
                environment.GetByteField(created.Handle, environment.GetField(owner, name: "tiny", descriptor: "B")) == -8,
                message: "Byte field failed"
            );
            environment.SetCharField(created.Handle, environment.GetField(owner, name: "letter", descriptor: "C"), value: '\uD800');
            Require(
                environment.GetCharField(created.Handle, environment.GetField(owner, name: "letter", descriptor: "C")) == '\uD800',
                message: "Char field failed"
            );
            environment.SetShortField(created.Handle, environment.GetField(owner, name: "small", descriptor: "S"), value: -100);
            Require(
                environment.GetShortField(created.Handle, environment.GetField(owner, name: "small", descriptor: "S")) == -100,
                message: "Short field failed"
            );
            environment.SetIntField(created.Handle, inherited, value: 27);
            Require(environment.GetIntField(created.Handle, inherited) == 27, message: "Inherited field write failed");
            environment.SetLongField(created.Handle, environment.GetField(owner, name: "large", descriptor: "J"), value: 1234567890123L);
            Require(
                environment.GetLongField(created.Handle, environment.GetField(owner, name: "large", descriptor: "J")) == 1234567890123L,
                message: "Long field failed"
            );
            environment.SetFloatField(created.Handle, environment.GetField(owner, name: "fraction", descriptor: "F"), value: 1.25f);
            Require(
                environment.GetFloatField(created.Handle, environment.GetField(owner, name: "fraction", descriptor: "F")) == 1.25f,
                message: "Float field failed"
            );
            environment.SetDoubleField(created.Handle, environment.GetField(owner, name: "precision", descriptor: "D"), value: -2.5);
            Require(
                environment.GetDoubleField(created.Handle, environment.GetField(owner, name: "precision", descriptor: "D")) == -2.5,
                message: "Double field failed"
            );

            nint shared = environment.GetField(owner, name: "shared", descriptor: "Ljava/lang/Object;", isStatic: true);

            using (JavaLocalReference empty = environment.GetObjectField(owner, shared, isStatic: true))
                Require(empty.Handle == 0, message: "Static null field failed");

            environment.SetObjectField(owner, shared, text.Handle, isStatic: true);

            using (JavaLocalReference set = environment.GetObjectField(owner, shared, isStatic: true))
                Require(environment.GetString(set.Handle) == "value", message: "Static object field failed");

            environment.SetObjectField(owner, shared, value: 0, isStatic: true);
            nint staticLarge = environment.GetField(owner, name: "staticLarge", descriptor: "J", isStatic: true);
            environment.SetLongField(owner, staticLarge, value: 123L, isStatic: true);
            Require(environment.GetLongField(owner, staticLarge, isStatic: true) == 123L, message: "Static primitive field failed");

            Require(
                environment.CallBoolean(created.Handle, environment.GetMethod(owner, name: "isReady", signature: "()Z"), []),
                message: "Boolean call failed"
            );
            Require(
                environment.CallByte(created.Handle, environment.GetMethod(owner, name: "byteValue", signature: "()B"), []) == -7,
                message: "Byte call failed"
            );
            Require(
                environment.CallChar(created.Handle, environment.GetMethod(owner, name: "charValue", signature: "()C"), []) == '\uD800',
                message: "Char call failed"
            );
            Require(
                environment.CallShort(created.Handle, environment.GetMethod(owner, name: "shortValue", signature: "()S"), []) == -11,
                message: "Short call failed"
            );
            Require(
                environment.CallLong(created.Handle, environment.GetMethod(owner, name: "longValue", signature: "()J"), []) == 1234567890123L,
                message: "Long call failed"
            );
            Require(
                environment.CallFloat(created.Handle, environment.GetMethod(owner, name: "floatValue", signature: "()F"), []) == 1.25f,
                message: "Float call failed"
            );
            Require(
                environment.CallDouble(created.Handle, environment.GetMethod(owner, name: "doubleValue", signature: "()D"), []) == -2.5,
                message: "Double call failed"
            );
            Require(
                environment.CallBoolean(owner, environment.GetMethod(owner, name: "staticReady", signature: "()Z", isStatic: true), [], isStatic: true),
                message: "Static boolean call failed"
            );
            Require(
                environment.CallLong(owner, environment.GetMethod(owner, name: "staticValue", signature: "()J", isStatic: true), [], isStatic: true) == 100L,
                message: "Static long call failed"
            );

            nint repeatMethod = environment.GetMethod(owner, name: "longValue", signature: "()J");

            for (int repeat = 0; repeat < 100; repeat++)
            {
                using JavaLocalReference empty = environment.GetObjectField(created.Handle, profile);

                Require(empty.Handle == 0 && environment.CallLong(created.Handle, repeatMethod, []) == 1234567890123L, message: "Repeated calls failed");
            }

            ExpectJavaException(
                () => { nint missingField = environment.GetField(owner, name: "absent", descriptor: "I"); Require(missingField == 0, message: "Missing field unexpectedly resolved"); }
            );
            ExpectJavaException(
                () => { nint missingMethod = environment.GetMethod(owner, name: "absent", signature: "()V"); Require(missingMethod == 0, message: "Missing method unexpectedly resolved"); }
            );
            ExpectJavaException(() => environment.CallVoid(created.Handle, environment.GetMethod(owner, name: "fail", signature: "()V"), []));
            ExpectJavaException(() => { using JavaLocalReference ignored = environment.NewObject(owner, constructor, [new jvalue { i = -1 }]); });
            JavaException detailed = CaptureJavaException(() => environment.CallVoid(created.Handle, environment.GetMethod(owner, name: "failDetailed", signature: "()V"), []));
            Require(detailed.Operation == nameof(JavaEnvironment.CallVoid), message: "Java call operation was lost");
            Require(
                detailed.JavaTypeName == "java.lang.IllegalStateException" && detailed.JavaMessage == "é 😀",
                message: "Unicode Java diagnostics were lost"
            );
            Require(
                detailed.JavaStackTrace?.Contains(value: "BridgeFixture$MemberChild.failDetailed(BridgeFixture.java:", StringComparison.Ordinal) == true,
                message: "Java stack location was lost"
            );
            Require(
                detailed.JavaStackTrace?.Contains(value: "Caused by: java.lang.IllegalArgumentException: cause Ω", StringComparison.Ordinal) == true,
                message: "Java cause was lost"
            );

            JavaException lookup = CaptureJavaException(
                () => { nint missingMethod = environment.GetMethod(owner, name: "absentDiagnostic", signature: "()V"); Require(missingMethod == 0, message: "Missing method unexpectedly resolved"); }
            );

            Require(
                lookup.Operation == nameof(JavaEnvironment.GetMethod) && lookup.JavaTypeName == "java.lang.NoSuchMethodError",
                message: "Java lookup diagnostics were lost"
            );
            JavaException construction = CaptureJavaException(() => { using JavaLocalReference ignored = environment.NewObject(owner, constructor, [new jvalue { i = -1 }]); });
            Require(
                construction.Operation == nameof(JavaEnvironment.NewObject) && construction.JavaTypeName == "java.lang.IllegalArgumentException" && construction.JavaMessage == "negative",
                message: "Java construction diagnostics were lost"
            );

            nint brokenDiagnostics = environment.GetMethod(owner, name: "failDiagnostic", signature: "()V");

            for (int repeat = 0; repeat < 100; repeat++)
            {
                JavaException failure = CaptureJavaException(() => environment.CallVoid(created.Handle, brokenDiagnostics, []));
                Require(
                    failure.Operation == nameof(JavaEnvironment.CallVoid) && failure.JavaTypeName == "BridgeFixture$DiagnosticFailure",
                    message: "Failed diagnostic method masked the original throwable"
                );
                Require(failure.JavaMessage is null && failure.JavaStackTrace is not null, message: "Failed diagnostic method corrupted the snapshot");
                Require(
                    environment.CallLong(created.Handle, repeatMethod, []) == 1234567890123L,
                    message: "JNI did not recover after diagnostic failure"
                );
            }

            Log(message: "MEMBER_INSPECTION_OK");
        }
        catch (Exception exception) when (ContainLoggingFailure(exception)) { Log($"NATIVE_ERROR {exception.Message}"); }
    }

    [UnmanagedCallersOnly]
    private static void InspectTwinTypes(JNINativeInterface_** nativeEnvironment, _jobject* caller, _jobject* first, _jobject* second)
    {
        try
        {
            if ((*nativeEnvironment)->ExceptionCheck(nativeEnvironment) != 0)
                return;

            AgentContext context = s_context ?? throw new InvalidOperationException(message: "Agent is not active.");

            using JavaEnvironment environment = new((nint)nativeEnvironment);

            Require(context.GetClassSignature((nint)first) == context.GetClassSignature((nint)second), message: "Twin signatures differed");

            using JavaLocalReference firstLoader = context.GetClassLoader(environment, (nint)first);

            using JavaLocalReference secondLoader = context.GetClassLoader(environment, (nint)second);

            Require(!environment.IsSameObject(firstLoader.Handle, secondLoader.Handle), message: "Twin loaders were identical");
            JavaFieldInfo firstField = context.GetDeclaredFields((nint)first).Single(static field => field.Name == "marker");
            JavaFieldInfo secondField = context.GetDeclaredFields((nint)second).Single(static field => field.Name == "marker");
            Require(
                firstField.Descriptor == secondField.Descriptor && firstField.Id != secondField.Id,
                message: "Field identifiers were not loader-specific"
            );
            JavaMethodInfo firstMethod = context.GetDeclaredMethods((nint)first).Single(static method => method.Name == "message");
            JavaMethodInfo secondMethod = context.GetDeclaredMethods((nint)second).Single(static method => method.Name == "message");
            Require(
                firstMethod.Descriptor == secondMethod.Descriptor && firstMethod.Id != secondMethod.Id,
                message: "Method identifiers were not loader-specific"
            );

            using JavaLocalReference marker = environment.NewString(value: "selected");

            environment.SetObjectField((nint)first, firstField.Id, marker.Handle, isStatic: true);

            using JavaLocalReference selected = environment.GetObjectField((nint)first, firstField.Id, isStatic: true);

            using JavaLocalReference other = environment.GetObjectField((nint)second, secondField.Id, isStatic: true);

            Require(
                environment.GetString(selected.Handle) == "selected" && environment.GetString(other.Handle) == "twin",
                message: "Twin fields crossed loaders"
            );
            Log(message: "LOADER_METADATA_OK");
        }
        catch (Exception exception) when (ContainLoggingFailure(exception)) { Log($"NATIVE_ERROR {exception.Message}"); }
    }

    private static void Log(string message)
    {
        try
        {
            Console.Error.WriteLine($"JvmBridge sample: {message}");
        }
        catch (Exception exception) when (ContainLoggingFailure(exception)) { return; }
    }

    [UnmanagedCallersOnly]
    private static void ObserveWeak(JNINativeInterface_** nativeEnvironment, _jobject* caller, _jobject* value)
    {
        try
        {
            if ((*nativeEnvironment)->ExceptionCheck(nativeEnvironment) != 0)
                return;

            using JavaEnvironment environment = new((nint)nativeEnvironment);

            BridgeAgent agent = s_agent ?? throw new InvalidOperationException(message: "Agent is not initialized.");
            JavaWeakGlobalReference weak = environment.NewWeakGlobalReference((nint)value);

            try
            {
                using JavaLocalReference original = environment.NewLocalReference((nint)value);

                using JavaGlobalReference global = original.ToGlobal();

                JavaVirtualMachine machine = environment.GetVirtualMachine();
                Exception? failure = null;

                Thread worker = new(
                    () =>
                {
                    try
                    {
                        using JavaThreadAttachment attachment = machine.AttachCurrentThread();

                        JavaEnvironment currentEnvironment = attachment.Environment;

                        using JavaLocalReference? promoted = weak.TryPromote(currentEnvironment);

                        if (promoted is null)
                            throw new InvalidOperationException(message: "Weak reference was collected while strongly reachable.");

                        using JavaLocalReference duplicate = currentEnvironment.NewLocalReference(global.Handle);

                        using JavaLocalReference different = currentEnvironment.NewString(value: "different");

                        Require(currentEnvironment.IsSameObject(promoted.Handle, duplicate.Handle), message: "Separate handles lost Java identity.");
                        Require(!currentEnvironment.IsSameObject(promoted.Handle, different.Handle), message: "Different objects share Java identity.");
                        Require(currentEnvironment.IsSameObject(first: 0, second: 0), message: "Two Java nulls did not compare equal.");
                        Require(!currentEnvironment.IsSameObject(promoted.Handle, second: 0), message: "A live object compared equal to Java null.");
                    }
                    catch (Exception exception) when (ContainLoggingFailure(exception)) { failure = exception; }
                }
                );

                worker.Start();
                worker.Join();

                if (failure is not null)
                    throw failure;

                agent._observedWeak?.Dispose();
                agent._observedWeak = weak;
            }
            catch
            {
                weak.Dispose();

                throw;
            }
        }
        catch (Exception exception) when (ContainLoggingFailure(exception)) { Log($"NATIVE_ERROR {exception.Message}"); }
    }

    [UnmanagedCallersOnly]
    private static void OnFrame(JNINativeInterface_** nativeEnvironment, _jobject* caller, _jobject* client)
    {
        try
        {
            if ((*nativeEnvironment)->ExceptionCheck(nativeEnvironment) != 0)
                return;

            using JavaEnvironment environment = new((nint)nativeEnvironment);

            if (s_context?.Options.Contains(value: "fail-helper", StringComparison.Ordinal) == true)
                throw new InvalidOperationException(message: "Intentional helper callback failure.");

            if (client == null)
            {
                Log(message: "HELPER_NULL_OK");

                return;
            }

            _jobject* clientType = environment.Functions->GetObjectClass(nativeEnvironment, client);
            environment.ThrowIfException(nameof(OnFrame));

            if (clientType == null)
                throw new InvalidOperationException(message: "GetObjectClass returned null.");

            try
            {
                nint ownerMethod = environment.GetMethod((nint)clientType, name: "owner", signature: "()Ljava/lang/Thread;");

                using JavaLocalReference owner = environment.CallObject((nint)client, ownerMethod, []);

                using JavaLocalReference threadType = environment.FindClass(name: "java/lang/Thread");

                nint currentMethod = environment.GetMethod(threadType.Handle, name: "currentThread", signature: "()Ljava/lang/Thread;", isStatic: true);

                using JavaLocalReference current = environment.CallObject(threadType.Handle, currentMethod, [], isStatic: true);

                if (!environment.IsSameObject(owner.Handle, current.Handle))
                    throw new InvalidOperationException(message: "Helper callback crossed Java threads.");
            }
            finally
            {
                environment.Functions->DeleteLocalRef(nativeEnvironment, clientType);
            }

            Log(message: "HELPER_THREAD_OK");
        }
        catch (Exception exception) when (ContainLoggingFailure(exception))
        {
            if (exception.Message == "Intentional helper callback failure.")
                Log(message: "HELPER_CALLBACK_CONTAINED");
            else
                Log($"NATIVE_ERROR {exception.Message}");
        }
    }

    private static void Register(JavaEnvironment environment, nint type)
    {
        environment.RegisterNative(
            type,
            name: "observeWeak",
            signature: "(Ljava/lang/Object;)V",
            (nint)(delegate* unmanaged<JNINativeInterface_**, _jobject*, _jobject*, void>)&ObserveWeak
        );
        environment.RegisterNative(
            type,
            name: "weakCollected",
            signature: "()Z",
            (nint)(delegate* unmanaged<JNINativeInterface_**, _jobject*, byte>)&WeakCollected
        );
        environment.RegisterNative(
            type,
            name: "roundTrip",
            signature: "(Ljava/lang/String;)Ljava/lang/String;",
            (nint)(delegate* unmanaged<JNINativeInterface_**, _jobject*, _jobject*, _jobject*>)&RoundTrip
        );
        environment.RegisterNative(
            type,
            name: "retransform",
            signature: "(Ljava/lang/Class;)Z",
            (nint)(delegate* unmanaged<JNINativeInterface_**, _jobject*, _jobject*, byte>)&Retransform
        );
        environment.RegisterNative(
            type,
            name: "registerTargetLoader",
            signature: "(Ljava/lang/ClassLoader;)V",
            (nint)(delegate* unmanaged<JNINativeInterface_**, _jobject*, _jobject*, void>)&RegisterTargetLoader
        );
        environment.RegisterNative(
            type,
            name: "registerHelperLoader",
            signature: "(Ljava/lang/ClassLoader;)V",
            (nint)(delegate* unmanaged<JNINativeInterface_**, _jobject*, _jobject*, void>)&RegisterHelperLoader
        );
        environment.RegisterNative(
            type,
            name: "clearTargetLoader",
            signature: "()V",
            (nint)(delegate* unmanaged<JNINativeInterface_**, _jobject*, void>)&ClearTargetLoader
        );
        environment.RegisterNative(
            type,
            name: "inspectMembers",
            signature: "(Ljava/lang/Class;)V",
            (nint)(delegate* unmanaged<JNINativeInterface_**, _jobject*, _jobject*, void>)&InspectMembers
        );
        environment.RegisterNative(
            type,
            name: "inspectTwinTypes",
            signature: "(Ljava/lang/Class;Ljava/lang/Class;)V",
            (nint)(delegate* unmanaged<JNINativeInterface_**, _jobject*, _jobject*, _jobject*, void>)&InspectTwinTypes
        );
        Log(message: "REGISTER_NATIVES");
    }

    [UnmanagedCallersOnly]
    private static void RegisterHelperLoader(JNINativeInterface_** nativeEnvironment, _jobject* caller, _jobject* loader)
    {
        try
        {
            if ((*nativeEnvironment)->ExceptionCheck(nativeEnvironment) != 0)
                return;

            using JavaEnvironment environment = new((nint)nativeEnvironment);

            BridgeAgent agent = s_agent ?? throw new InvalidOperationException(message: "Agent is not active.");
            agent.InstallHelper(environment, (nint)loader);
        }
        catch (Exception exception) when (ContainLoggingFailure(exception)) { Log($"NATIVE_ERROR {exception.Message}"); }
    }

    [UnmanagedCallersOnly]
    private static void RegisterTargetLoader(JNINativeInterface_** nativeEnvironment, _jobject* caller, _jobject* loader)
    {
        try
        {
            if ((*nativeEnvironment)->ExceptionCheck(nativeEnvironment) != 0)
                return;

            BridgeAgent agent = s_agent ?? throw new InvalidOperationException(message: "Agent is not active.");

            if (agent._targetLoader is not null)
                throw new InvalidOperationException(message: "Target loader is already registered.");

            using JavaEnvironment environment = new((nint)nativeEnvironment);

            using JavaLocalReference local = environment.NewLocalReference((nint)loader);

            agent._targetLoader = local.ToGlobal();
        }
        catch (Exception exception) when (ContainLoggingFailure(exception)) { Log($"NATIVE_ERROR {exception.Message}"); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    [UnmanagedCallersOnly]
    private static byte Retransform(JNINativeInterface_** environment, _jobject* caller, _jobject* type)
    {
        try
        {
            if (s_context is not { CanRetransform: true } context)
                return 0;

            context.Retransform((nint)type);

            return 1;
        }
        catch (Exception exception) when (ContainLoggingFailure(exception))
        {
            Log($"NATIVE_ERROR {exception.Message}");

            return 0;
        }
    }

    [UnmanagedCallersOnly]
    private static _jobject* RoundTrip(JNINativeInterface_** nativeEnvironment, _jobject* type, _jobject* value)
    {
        try
        {
            if ((*nativeEnvironment)->ExceptionCheck(nativeEnvironment) != 0)
                return null;

            using JavaEnvironment environment = new((nint)nativeEnvironment);

            using JavaLocalReference local = environment.NewLocalReference((nint)value);

            using JavaGlobalReference global = local.ToGlobal();

            string? result = null;
            Exception? failure = null;
            JavaVirtualMachine machine = environment.GetVirtualMachine();

            Thread worker = new(
                () =>
            {
                try
                {
                    using JavaThreadAttachment attachment = machine.AttachCurrentThread();

                    result = attachment.Environment.GetString(global.Handle);
                }
                catch (Exception exception) when (ContainLoggingFailure(exception)) { failure = exception; }
            }
            );

            worker.Start();
            worker.Join();

            if (failure is not null)
                throw failure;

            using JavaLocalReference converted = environment.NewString(result ?? string.Empty);

            return environment.Functions->NewLocalRef(nativeEnvironment, (_jobject*)converted.Handle);
        }
        catch (Exception exception) when (ContainLoggingFailure(exception))
        {
            Log($"NATIVE_ERROR {exception.Message}");

            return null;
        }
    }

    [UnmanagedCallersOnly]
    private static byte WeakCollected(JNINativeInterface_** nativeEnvironment, _jobject* caller)
    {
        try
        {
            if ((*nativeEnvironment)->ExceptionCheck(nativeEnvironment) != 0)
                return 0;

            using JavaEnvironment environment = new((nint)nativeEnvironment);

            JavaWeakGlobalReference weak = s_agent?._observedWeak ?? throw new InvalidOperationException(message: "No weak reference was observed.");

            using JavaLocalReference? promoted = weak.TryPromote(environment);

            return promoted is null ? (byte)1 : (byte)0;
        }
        catch (Exception exception) when (ContainLoggingFailure(exception))
        {
            Log($"NATIVE_ERROR {exception.Message}");

            return 0;
        }
    }

    private void InstallHelper(JavaEnvironment environment, nint loader)
    {
        ArgumentOutOfRangeException.ThrowIfZero(loader);

        if (_helpers.Any(helper => environment.IsSameObject(helper.Loader.Handle, loader)))
            return;

        byte[] bytes = CallbackClassFile.Generate();

        using JavaLocalReference helper = environment.DefineClass(CallbackClassFile.Name, loader, bytes);

        environment.RegisterNative(
            helper.Handle,
            name: "onFrame",
            signature: "(Ljava/lang/Object;)V",
            (nint)(delegate* unmanaged<JNINativeInterface_**, _jobject*, _jobject*, void>)&OnFrame
        );

        using JavaLocalReference localLoader = environment.NewLocalReference(loader);

        JavaGlobalReference globalLoader = localLoader.ToGlobal();

        try
        {
            _helpers.Add((globalLoader, helper.ToGlobal()));
        }
        catch
        {
            globalLoader.Dispose();

            throw;
        }

        Log(message: "HELPER_INSTALLED");

        try
        {
            using JavaLocalReference duplicate = environment.DefineClass(CallbackClassFile.Name, loader, bytes);

            throw new InvalidOperationException(message: "Duplicate helper class definition unexpectedly succeeded.");
        }
        catch (JavaException exception) when (exception.Operation == nameof(JavaEnvironment.DefineClass))
        {
            Log(message: "HELPER_DUPLICATE_REJECTED");
        }
    }

    private void InstallSystemHelper(JavaEnvironment environment)
    {
        using JavaLocalReference loaderType = environment.FindClass(name: "java/lang/ClassLoader");

        nint method = environment.GetMethod(loaderType.Handle, name: "getSystemClassLoader", signature: "()Ljava/lang/ClassLoader;", isStatic: true);

        using JavaLocalReference loader = environment.CallObject(loaderType.Handle, method, [], isStatic: true);

        InstallHelper(environment, loader.Handle);
    }
}
