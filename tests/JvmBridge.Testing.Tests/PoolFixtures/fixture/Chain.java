package fixture;

import java.util.function.BiConsumer;

/**
 * javac 8 writes two Class entries for BiConsumer here: one for the local that the stack map frame after the if names, and one that the andThen reference goes through.
 */
public class Chain {
    public static void run(BiConsumer<String, String> consumer) {
        BiConsumer<String, String> both = consumer;
        if (both != null)
            both = both.andThen(consumer);
        both.accept("a", "b");
    }
}
