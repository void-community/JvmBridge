package legacy;

import java.util.Collections;
import java.util.HashSet;
import java.util.Set;

/**
 * Helpers that name the application's own types, compiled against a stub of them and defined in the application's class loader, in its package. A patch redirects Account.deposit here, and another makes NAMES come from newNames.
 */
public final class TypedHelpers {
    private TypedHelpers() {
    }

    public static int deposit(LegacyFixture.Account account, int amount) {
        return account.balance + amount * 100 + account.legacy$bonus();
    }

    public static Set<String> newNames() {
        return Collections.synchronizedSet(new HashSet<String>());
    }
}
