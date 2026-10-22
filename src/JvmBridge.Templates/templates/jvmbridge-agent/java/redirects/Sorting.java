package redirects;

import java.util.Comparator;
import java.util.List;

/** Compiled with javac at build time, embedded in the agent, and defined in the JVM when the agent loads. */
public final class Sorting {
    /** Sorts in the opposite order of the comparator, so the effect of the patch is easy to see. */
    @SuppressWarnings({"unchecked", "rawtypes"})
    public static void sort(List list, Comparator comparator) {
        java.util.Collections.sort(list, java.util.Collections.reverseOrder(comparator));
    }
}
