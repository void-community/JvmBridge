package example;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.Comparator;
import java.util.List;

/** A stand-in for the class your agent patches. Replace it with a fixture that reproduces the behavior you change. */
public final class Target {
    public static void sorted(List<String> values) {
        Collections.sort(values, Comparator.<String>naturalOrder());
    }

    public static void main(String[] arguments) {
        List<String> values = new ArrayList<String>(Arrays.asList("b", "c", "a"));
        sorted(values);
        System.out.println("RESULT=" + values);
    }
}
