import java.lang.reflect.Method;

/** The program LegacyAgent patches, in the Java 6 language, so that it compiles for and runs on Java 6 and later. */
public final class LegacyFixture {
    private LegacyFixture() {
    }

    static String greeting() {
        return "legacy-original";
    }

    static int length(String value) {
        return value.length();
    }

    static int add(int left, int right) {
        return left + right;
    }

    public static void main(String[] arguments) throws Exception {
        System.out.println("JAVA " + System.getProperty("java.specification.version"));
        System.out.println("legacy-patched".equals(greeting()) ? "PATCH_OK" : "PATCH_FAIL " + greeting());
        System.out.println(length("abc") == 1003 ? "HELPER_OK" : "HELPER_FAIL " + length("abc"));
        System.out.println(add(2, 3) == 1005 ? "ADVICE_OK" : "ADVICE_FAIL " + add(2, 3));

        Thread worker = new Thread(new Runnable() {
            public void run() {
            }
        }, "legacy-worker");
        worker.start();
        worker.join();

        // The helper is defined in the bootstrap loader, which the application loader delegates to.
        Method events = Class.forName("jvmbridge.legacy.Interop").getMethod("events");
        String summary = (String) events.invoke(null);
        boolean counted = !summary.contains("threadStart=0;") && !summary.contains("classLoad=0;");
        System.out.println(counted && summary.contains("attach=ok") ? "EVENTS_OK " + summary : "EVENTS_FAIL " + summary);
    }
}
