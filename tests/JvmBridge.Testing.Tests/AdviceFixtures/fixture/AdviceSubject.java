package fixture;

public class AdviceSubject {
    public int field = 7;

    public static int staticInt(int a, long b) { return a + (int) b; }
    public static long staticLong(double d, String s) { return (long) d + s.length(); }
    public static double staticDouble(float f, int[] values, Object o) { return f + values.length; }
    public static void staticVoid(StringBuilder builder) { builder.append("v"); }
    public static String staticString(String s) { return s + "!"; }
    public static int[] staticArray(int n) { return new int[n]; }

    public int instanceInt(int a) { return a + field; }
    public long instanceLong(long a, long b) { return a + b + field; }
    public String instanceString(String s, char c, boolean z, byte by, short sh) { return s + c + z + by + sh; }
    public void instanceVoid(String s) { Log.add("body:" + s); }
    public synchronized int syncMethod(int a) { return a * 2; }

    public int withTry(int a) {
        try {
            if (a < 0) throw new IllegalStateException("negative");
            return a;
        } catch (IllegalStateException e) {
            return -1;
        } finally {
            Log.add("finally");
        }
    }

    @Deprecated
    public int annotated(int a) { return a; }

    public int thrower(int a) { throw new IllegalArgumentException("thrown " + a); }
}
