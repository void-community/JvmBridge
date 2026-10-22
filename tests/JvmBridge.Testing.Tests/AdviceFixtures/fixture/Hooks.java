package fixture;

public final class Hooks {
    private Hooks() { }

    public static void beforeStaticInt(Object self, int a, long b) { Log.add("b:staticInt:" + self + ":" + a + ":" + b); }
    public static int afterStaticInt(Object self, int result) { return result + 1000; }

    public static long afterStaticLong(Object self, double d, String s, long result) { return result * 2; }

    public static void beforeStaticDouble(Object self, float f, int[] values, Object o) { Log.add("b:staticDouble:" + f + ":" + values.length + ":" + o); }

    public static void beforeStaticVoid(Object self, StringBuilder builder) { Log.add("b:staticVoid:" + builder.length()); }
    public static void afterStaticVoid(Object self) { Log.add("a:staticVoid"); }

    public static String afterStaticString(Object self, String result) { return result + "?"; }

    public static int[] afterStaticArray(Object self, int[] result) { return new int[result.length + 1]; }

    public static void beforeInstanceInt(AdviceSubject self, int a) { Log.add("b:instanceInt:" + a + ":" + self.field); }
    public static int afterInstanceInt(Object self, int result) { return result + 1; }

    public static long afterInstanceLong(Object self, long a, long b, long result) { return result + a - b; }

    public static void beforeInstanceString(Object self, String s, char c, boolean z, byte by, short sh) { Log.add("b:instanceString:" + s + c + z + by + sh); }

    public static void beforeInstanceVoid(Object self, String s) { Log.add("b:instanceVoid:" + s); }
    public static void afterInstanceVoid(Object self) { Log.add("a:instanceVoid"); }

    public static void beforeSyncMethod(Object self, int a) { Log.add("b:sync:" + Thread.holdsLock(self)); }
    public static int afterSyncMethod(Object self, int result) { Log.add("a:sync:" + Thread.holdsLock(self)); return result; }

    public static int afterWithTry(Object self, int result) { Log.add("a:withTry:" + result); return result; }

    public static int afterAnnotated(Object self, int result) { return result + 1; }

    public static void beforeThrower(Object self, int a) { Log.add("b:thrower"); }
    public static int afterThrower(Object self, int result) { Log.add("a:thrower"); return result; }
}
