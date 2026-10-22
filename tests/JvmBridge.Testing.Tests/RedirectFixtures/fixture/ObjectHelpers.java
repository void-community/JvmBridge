package fixture;

import java.util.List;

/** Stands in for a helper in the bootstrap loader, which cannot see the types it is given, so it takes Object. */
public final class ObjectHelpers {
    private ObjectHelpers() { }

    public static int length(Object value) { return ((String) value).length() + 1; }

    public static int size(Object values) { return ((List<?>) values).size() + 10; }
}
