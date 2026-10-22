package fixture;

import java.util.Arrays;
import java.util.List;

public class RedirectSubject {
    public static int measure(String value, List<String> values) {
        return value.length() * 100 + values.size();
    }

    public static void main(String[] arguments) {
        System.out.println("RESULT=" + measure("abc", Arrays.asList("x", "y")));
    }
}
