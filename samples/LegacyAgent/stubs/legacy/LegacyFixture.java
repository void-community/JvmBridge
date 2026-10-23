package legacy;

/**
 * A stand-in for the classes of the application that TypedHelpers names, compiled with it but not embedded. The application's Account is package-private with a private balance; the stub makes the field public so that the helper compiles,
 * and SetFieldAccess opens it in the real class when it loads.
 */
public final class LegacyFixture {
    private LegacyFixture() {
    }

    public static final class Account {
        public int balance;
    }
}
