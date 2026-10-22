import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.net.URL;
import java.security.CodeSource;
import java.security.ProtectionDomain;

public final class BridgeFixture {
    private static native String roundTrip(String value);
    private static native boolean retransform(Class<?> type);
    private static native void registerTargetLoader(ClassLoader loader);
    private static native void registerHelperLoader(ClassLoader loader);
    private static native void clearTargetLoader();
    private static native void inspectMembers(Class<?> type);
    private static native void inspectTwinTypes(Class<?> first, Class<?> second);
    private static native void observeWeak(Object value);
    private static native boolean weakCollected();
    private static String message() { return "ORIGINAL"; }
    public static void probe(Object client) { client.hashCode(); }

    public static final class Client {
        private final Thread thread;

        Client(Thread thread) { this.thread = thread; }
        public Thread owner() { return thread; }
    }

    public static final class Twin {
        private static String marker = "twin";
        public static String message() { return "ORIGINAL"; }
        public static void probe(Object client) { client.hashCode(); }
    }

    public static final class PatchTarget {
        public static int length(String value) { return value.length(); }
        public static java.util.List<String> sorted(java.util.List<String> list) {
            java.util.Collections.sort(list, java.util.Collections.<String>reverseOrder());
            return list;
        }
        public static String label() { return "patch-original"; }
    }

    private static final class PatchLoader extends ClassLoader {
        PatchLoader() { super(null); }

        Class<?> definePatchTarget(byte[] bytes) { return defineClass("BridgeFixture$PatchTarget", bytes, 0, bytes.length); }
    }

    private static class MemberBase {
        private int inherited = 19;
    }

    private static final class DiagnosticFailure extends RuntimeException {
        @Override public String getMessage() { throw new IllegalStateException("message failed"); }
        @Override public String toString() { throw new IllegalStateException("toString failed"); }
        @Override public StackTraceElement[] getStackTrace() { throw new IllegalStateException("stack failed"); }
        @Override public synchronized Throwable getCause() { throw new IllegalStateException("cause failed"); }
    }

    private static final class MemberChild extends MemberBase {
        private Object profile;
        private boolean flag;
        private byte tiny;
        private char letter;
        private short small;
        private int count;
        private long large;
        private float fraction;
        private double precision;
        private java.util.List<String> generic;
        private int λ = 23;
        private static Object shared;
        private static long staticLarge;

        private MemberChild(int value) {
            if (value < 0) throw new IllegalArgumentException("negative");
            count = value;
        }

        private boolean isReady() { return true; }
        private byte byteValue() { return -7; }
        private char charValue() { return '\uD800'; }
        private short shortValue() { return -11; }
        private long longValue() { return 1234567890123L; }
        private float floatValue() { return 1.25f; }
        private double doubleValue() { return -2.5; }
        private java.util.List<String> genericMethod() { return generic; }
        private void fail() { throw new IllegalArgumentException("expected"); }
        private void failDetailed() { throw new IllegalStateException("é \uD83D\uDE00", new IllegalArgumentException("cause Ω")); }
        private void failDiagnostic() { throw new DiagnosticFailure(); }
        private static boolean staticReady() { return true; }
        private static long staticValue() { return 100L; }
    }

    private static final class TwinLoader extends ClassLoader {
        private final String location;

        TwinLoader(String location) { super(null); this.location = location; }

        Class<?> defineTarget(byte[] bytes) throws Exception {
            ProtectionDomain domain = new ProtectionDomain(new CodeSource(new URL(location), (java.security.cert.Certificate[])null), null, this, null);
            return defineClass("BridgeFixture$Twin", bytes, 0, bytes.length, domain);
        }
    }

    private static byte[] twinBytes() throws Exception { return readClass("BridgeFixture$Twin.class"); }

    private static byte[] readClass(String resource) throws Exception {
        try (InputStream stream = BridgeFixture.class.getResourceAsStream(resource)) {
            if (stream == null) throw new AssertionError("Missing class bytes " + resource);
            ByteArrayOutputStream output = new ByteArrayOutputStream();
            byte[] buffer = new byte[4096];
            int count;
            while ((count = stream.read(buffer)) != -1) output.write(buffer, 0, count);
            return output.toByteArray();
        }
    }

    private static String twinMessage(Class<?> type) throws Exception {
        return (String)type.getMethod("message").invoke(null);
    }

    private static String originalMessage() {
        return new StringBuilder("ORIG").append("INAL").toString();
    }

    private static java.lang.ref.WeakReference<Object> startWeakProbe() {
        Object value = new Object();
        java.lang.ref.WeakReference<Object> probe = new java.lang.ref.WeakReference<>(value);
        observeWeak(value);
        return probe;
    }

    private static void checkWeakReference() throws Exception {
        java.lang.ref.WeakReference<Object> probe = startWeakProbe();
        for (int attempt = 0; attempt < 80; attempt++) {
            if (probe.get() == null && weakCollected()) {
                System.out.println("WEAK_REFERENCE_OK");
                return;
            }
            System.gc();
            byte[] pressure = new byte[256 * 1024];
            pressure[0] = (byte)attempt;
            Thread.sleep(20);
        }
        throw new AssertionError("Weak JNI reference retained its referent or failed to report collection");
    }

    private static void checkLoaderIsolation() throws Exception {
        byte[] bytes = twinBytes();
        TwinLoader selected = new TwinLoader("file:/jvmbridge-selected.jar");
        TwinLoader other = new TwinLoader("file:/jvmbridge-other.jar");
        registerHelperLoader(selected);
        registerHelperLoader(selected);
        registerHelperLoader(other);
        registerHelperLoader(other);
        registerTargetLoader(selected);
        try {
            Class<?> target = selected.defineTarget(bytes);
            Class<?> ignored = other.defineTarget(bytes);
            Class<?> helperFromSelected = Class.forName("jvmbridge.sample.AgentCallbacks", false, selected);
            Class<?> helperFromOther = Class.forName("jvmbridge.sample.AgentCallbacks", false, other);
            if (helperFromSelected == helperFromOther || helperFromSelected.getClassLoader() != selected || helperFromOther.getClassLoader() != other)
                throw new AssertionError("Helper is not defined in both isolated loaders");
            System.out.println("HELPER_LOADER_VISIBILITY_OK");
            String selectedMessage = twinMessage(target);
            String otherMessage = twinMessage(ignored);
            inspectTwinTypes(target, ignored);
            if (!"MODIFIED".equals(selectedMessage) || !originalMessage().equals(otherMessage))
                throw new AssertionError("Identical class names were not distinguished by loader: " + selectedMessage + ", " + otherMessage);
            System.out.println("LOADER_ISOLATION_OK");
            target.getMethod("probe", Object.class).invoke(null, new Client(Thread.currentThread()));
            System.out.println("HELPER_TWIN_OK");
            if (retransform(target)) {
                if (!"RELOADED".equals(twinMessage(target)) || !originalMessage().equals(twinMessage(ignored)))
                    throw new AssertionError("Selected loader retransformation failed");
                System.out.println("LOADER_RETRANSFORM_OK");
            }
        } finally { clearTargetLoader(); }
    }

    private static void checkPatches(boolean attached, boolean retransformable) throws Exception {
        if (attached && !retransformable) {
            System.out.println("PATCH_RETRANSFORM_UNAVAILABLE");
        } else {
            java.util.List<String> values = new java.util.ArrayList<String>(java.util.Arrays.asList("b", "a", "c"));
            int length = PatchTarget.length("abcd");
            String label = PatchTarget.label();
            java.util.List<String> sorted = PatchTarget.sorted(values);
            if (length != 1004 || !"patch-replaced".equals(label) || !java.util.Arrays.asList("c", "a", "b").equals(sorted))
                throw new AssertionError("Patches did not apply: " + length + " " + label + " " + sorted);
            System.out.println("PATCH_OK");
        }
        Class<?> bootstrapHelper = Class.forName("jvmbridge.sample.BootstrapPatchHelpers", false, null);
        if (bootstrapHelper.getClassLoader() != null) throw new AssertionError("Bootstrap helper had a defining loader");
        System.out.println("PATCH_BOOTSTRAP_HELPER_OK");
        byte[] bytes = readClass("BridgeFixture$PatchTarget.class");
        PatchLoader first = new PatchLoader();
        PatchLoader second = new PatchLoader();
        Class<?> firstTarget = first.definePatchTarget(bytes);
        Class<?> secondTarget = second.definePatchTarget(bytes);
        for (Class<?> target : new Class<?>[] { firstTarget, secondTarget }) {
            if (!Integer.valueOf(1004).equals(target.getMethod("length", String.class).invoke(null, "abcd")))
                throw new AssertionError("Isolated loader did not get the patched call");
            if (!"patch-replaced".equals(target.getMethod("label").invoke(null)))
                throw new AssertionError("Isolated loader did not get the replaced constant");
        }
        Class<?> firstHelper = Class.forName("jvmbridge.sample.LoaderPatchHelpers", false, first);
        Class<?> secondHelper = Class.forName("jvmbridge.sample.LoaderPatchHelpers", false, second);
        if (firstHelper == secondHelper || firstHelper.getClassLoader() != first || secondHelper.getClassLoader() != second)
            throw new AssertionError("The loader helper was not defined once in each patched loader");
        System.out.println("PATCH_LOADER_HELPERS_OK");
    }

    private static void checkBootstrapLoader() throws Exception {
        Class<?> bootstrap = Class.forName("java.util.concurrent.atomic.AtomicStampedReference", false, null);
        if (bootstrap.getClassLoader() != null) throw new AssertionError("Bootstrap class had a defining loader");
        System.out.println("BOOTSTRAP_NULL_LOADER_OK");
    }

    public static void main(String[] arguments) throws Exception {
        if (arguments.length > 0 && arguments[0].equals("baseline")) {
            System.out.println("JAVA_BASELINE_OK");
            return;
        }
        boolean attach = arguments.length > 0 && arguments[0].equals("attach");
        if (attach) {
            if (PatchTarget.length("touch") != 5) throw new AssertionError("PatchTarget was patched before the agent attached");
            System.out.println("READY");
            System.out.flush();
            if (System.in.read() < 0) throw new AssertionError("Attach controller closed stdin");
        } else {
            if (!"true".equals(System.getProperty("jvmbridge.loaded"))) throw new AssertionError("VMInit did not run");
            if (!"MODIFIED".equals(message())) throw new AssertionError("Load transformation did not run: " + message());
        }
        probe(null);
        inspectMembers(new MemberChild(1).getClass());
        probe(new Client(Thread.currentThread()));
        Thread helperWorker = new Thread(new Runnable() {
            public void run() { probe(new Client(Thread.currentThread())); }
        }, "jvmbridge-helper-worker");
        helperWorker.start();
        helperWorker.join();
        System.out.println("HELPER_CALLS_OK");
        String[] values = {"ASCII", "A\u0000B", "\uD83D\uDE00", "\uD800", "", "\u03BB\u4E16\u754C"};
        for (int repeat = 0; repeat < 100; repeat++)
            for (String value : values)
                if (!value.equals(roundTrip(value))) throw new AssertionError("JNI Unicode round trip failed");
        boolean retransformable = retransform(BridgeFixture.class);
        if (retransformable) {
            if (!"RELOADED".equals(message())) throw new AssertionError("Retransformation did not run");
            System.out.println("RETRANSFORM_OK");
        } else {
            System.out.println("RETRANSFORM_UNAVAILABLE");
        }
        checkPatches(attach, retransformable);
        checkBootstrapLoader();
        checkLoaderIsolation();
        checkWeakReference();
        for (int iteration = 0; iteration < 10000; iteration++) {
            try { Object value = null; value.hashCode(); throw new AssertionError("Missing NPE"); }
            catch (NullPointerException expected) { }
            try { int zero = Integer.parseInt("0"); int result = 1 / zero; throw new AssertionError(result); }
            catch (ArithmeticException expected) { }
        }
        System.gc();
        System.out.println("SIGNAL_EXCEPTIONS_OK");
        System.out.println("JNI_UNICODE_THREADS_REFERENCES_OK");
        System.out.println("AGENT_OK");
    }
}
