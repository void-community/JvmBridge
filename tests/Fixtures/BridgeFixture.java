import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.net.URL;
import java.security.CodeSource;
import java.security.ProtectionDomain;

public final class BridgeFixture {
    private static native String roundTrip(String value);
    private static native boolean retransform(Class<?> type);
    private static native void registerTargetLoader(ClassLoader loader);
    private static native void clearTargetLoader();
    private static String message() { return "ORIGINAL"; }

    public static final class Twin {
        public static String message() { return "ORIGINAL"; }
    }

    private static final class TwinLoader extends ClassLoader {
        private final String location;

        TwinLoader(String location) { super(null); this.location = location; }

        Class<?> defineTarget(byte[] bytes) throws Exception {
            ProtectionDomain domain = new ProtectionDomain(new CodeSource(new URL(location), (java.security.cert.Certificate[])null), null, this, null);
            return defineClass("BridgeFixture$Twin", bytes, 0, bytes.length, domain);
        }
    }

    private static byte[] twinBytes() throws Exception {
        try (InputStream stream = BridgeFixture.class.getResourceAsStream("BridgeFixture$Twin.class")) {
            if (stream == null) throw new AssertionError("Missing twin class bytes");
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

    private static void checkLoaderIsolation() throws Exception {
        byte[] bytes = twinBytes();
        TwinLoader selected = new TwinLoader("file:/jvmbridge-selected.jar");
        TwinLoader other = new TwinLoader("file:/jvmbridge-other.jar");
        registerTargetLoader(selected);
        try {
            Class<?> target = selected.defineTarget(bytes);
            Class<?> ignored = other.defineTarget(bytes);
            String selectedMessage = twinMessage(target);
            String otherMessage = twinMessage(ignored);
            if (!"MODIFIED".equals(selectedMessage) || !originalMessage().equals(otherMessage))
                throw new AssertionError("Identical class names were not distinguished by loader: " + selectedMessage + ", " + otherMessage);
            System.out.println("LOADER_ISOLATION_OK");
            if (retransform(target)) {
                if (!"RELOADED".equals(twinMessage(target)) || !originalMessage().equals(twinMessage(ignored)))
                    throw new AssertionError("Selected loader retransformation failed");
                System.out.println("LOADER_RETRANSFORM_OK");
            }
        } finally { clearTargetLoader(); }
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
            System.out.println("READY");
            System.out.flush();
            if (System.in.read() < 0) throw new AssertionError("Attach controller closed stdin");
        } else {
            if (!"true".equals(System.getProperty("jvmbridge.loaded"))) throw new AssertionError("VMInit did not run");
            if (!"MODIFIED".equals(message())) throw new AssertionError("Load transformation did not run: " + message());
        }
        String[] values = {"ASCII", "A\u0000B", "\uD83D\uDE00", "\uD800", "", "\u03BB\u4E16\u754C"};
        for (int repeat = 0; repeat < 100; repeat++)
            for (String value : values)
                if (!value.equals(roundTrip(value))) throw new AssertionError("JNI Unicode round trip failed");
        if (retransform(BridgeFixture.class)) {
            if (!"RELOADED".equals(message())) throw new AssertionError("Retransformation did not run");
            System.out.println("RETRANSFORM_OK");
        } else {
            System.out.println("RETRANSFORM_UNAVAILABLE");
        }
        checkBootstrapLoader();
        checkLoaderIsolation();
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
