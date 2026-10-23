package jvmbridge.legacy;

/** The advice hook of LegacyFixture.add, compiled for Java 6 from a JavaSource item. */
public final class Hooks {
    private Hooks() {
    }

    public static int afterAdd(Object self, int result) {
        return result + 1000;
    }
}
