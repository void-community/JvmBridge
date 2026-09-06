public final class BridgeFixture {
    private static native String roundTrip(String value);
    private static native boolean retransform(Class<?> type);
    private static String message() { return "ORIGINAL"; }

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
