package fixture;

import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Set;

/** Creates collections in field initializers and configures a builder fluently, in the Java 6 language, for the construction and removal edits. */
public final class ConstructionSubject {
    private final List<String> sent = new ArrayList<String>();
    private final List<String> other = new ArrayList<String>();
    private final Set<String> names = new HashSet<String>();

    public static void main(String[] arguments) {
        ConstructionSubject subject = new ConstructionSubject();
        Config config = new Config().autosave().autoreload();
        System.out.println("SENT=" + subject.sent.getClass().getName());
        System.out.println("OTHER=" + subject.other.getClass().getName());
        System.out.println("NAMES=" + subject.names.getClass().getName());
        System.out.println("FLAGS=" + config.saving + "," + config.reloading);
    }
}

final class Config {
    boolean saving;
    boolean reloading;

    Config autosave() {
        saving = true;
        return this;
    }

    Config autoreload() {
        reloading = true;
        return this;
    }
}
