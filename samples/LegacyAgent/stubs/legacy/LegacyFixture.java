package legacy;

/**
 * A stand-in for the classes of the application that TypedHelpers names, compiled with it but not embedded. The application's Account is package-private with a private balance and a private bonus(); the stub makes the field public, which
 * SetFieldAccess opens in the real class when it loads, and declares the invoker that the agent adds for bonus().
 */
public final class LegacyFixture {
    private LegacyFixture() {
    }

    public static final class Account {
        public int balance;

        public int legacy$bonus() {
            throw new UnsupportedOperationException();
        }
    }
}
