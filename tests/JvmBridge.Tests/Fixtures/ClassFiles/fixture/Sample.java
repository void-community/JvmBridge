package fixture;

import java.util.Collections;
import java.util.Comparator;
import java.util.List;

/** Compiled with {@code javac --release 8 -g:none}; the checked-in Sample.class is its output. */
public class Sample {
    public static final int LIMIT = 100000;
    public static final long BIG = 5000000000L;
    public static final float SCALE = 2.5f;
    public static final double RATIO = 3.5;
    public static final String GREETING = "hello";
    private int count;
    public static volatile String shared = "shared";

    public static void sortTwice(List<String> list, Comparator<String> comparator) {
        Collections.sort(list, comparator);
        Collections.sort(list, comparator);
    }

    public static void sortOnce(List<String> list, Comparator<String> comparator) {
        Collections.sort(list, comparator);
    }

    public static int length(String value) {
        return value.length();
    }

    public static int size(List<String> list) {
        return list.size();
    }

    public static String greeting() {
        return "hello";
    }

    public static String other() {
        return "hello again";
    }

    public static int limit() {
        return 100000;
    }

    public static long big() {
        return 5000000000L;
    }

    public static float scale() {
        return 2.5f;
    }

    public static double ratio() {
        return 3.5;
    }

    public static String unicode() {
        return "héllo ☃ 😀 \u0000 end";
    }

    public static int dense(int value) {
        switch (value) {
            case 1: return 10;
            case 2: return 20;
            case 3: return 30;
            case 4: return 40;
            default: return 0;
        }
    }

    public static int sparse(int value) {
        switch (value) {
            case 1: return 10;
            case 100: return 20;
            case 10000: return 30;
            default: return 0;
        }
    }

    public int count() {
        return count++;
    }
}
