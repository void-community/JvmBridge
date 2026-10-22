package fixture;

import java.util.ArrayList;
import java.util.List;

public final class Log {
    public static final List<String> lines = new ArrayList<String>();

    public static void add(String line) {
        lines.add(line);
    }
}
