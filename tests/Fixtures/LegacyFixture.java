package legacy;

import java.lang.reflect.Method;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Set;

/** The program LegacyAgent patches, in the Java 6 language, so that it compiles for and runs on Java 6 and later. */
public final class LegacyFixture {
    private static final List<String> SENT = new ArrayList<String>();
    private static final Set<String> NAMES = new HashSet<String>();

    private LegacyFixture() {
    }

    /** An application class whose private state a typed helper reads once a patch opens it. */
    static final class Account {
        private int balance = 10;

        int deposit(int amount) {
            balance += amount;
            return balance;
        }

        private int bonus() {
            return 7;
        }
    }

    /** A builder with a fluent setter, which a patch removes. */
    static final class Settings {
        boolean saving;

        Settings autosave() {
            saving = true;
            return this;
        }
    }

    static int deposited() {
        return new Account().deposit(5);
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
        System.out.println(deposited() == 517 ? "TYPED_OK" : "TYPED_FAIL " + deposited());
        System.out.println("java.util.concurrent.CopyOnWriteArrayList".equals(SENT.getClass().getName()) ? "CONSTRUCTION_OK" : "CONSTRUCTION_FAIL " + SENT.getClass().getName());
        System.out.println("java.util.Collections$SynchronizedSet".equals(NAMES.getClass().getName()) ? "FACTORY_OK" : "FACTORY_FAIL " + NAMES.getClass().getName());
        Settings settings = new Settings().autosave();
        System.out.println(settings.saving ? "REMOVAL_FAIL" : "REMOVAL_OK");

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
