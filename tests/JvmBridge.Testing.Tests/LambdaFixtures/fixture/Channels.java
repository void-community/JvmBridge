package fixture;

import java.util.HashSet;
import java.util.Set;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.function.Function;

// Shaped like Minecraft's ChannelAccess: an executor that other methods hand their work to through lambdas, and a clear() that does its work on the calling thread.
public class Channels {
    private final Set<String> channels = new HashSet<>();
    private final ExecutorService executor = Executors.newSingleThreadExecutor(task -> new Thread(task, "Sound engine"));

    public void add(String name) {
        executor.execute(() -> channels.add(name));
    }

    public void clear() {
        System.out.println("clear on " + Thread.currentThread().getName());
        channels.forEach(name -> System.out.println("release " + name));
        channels.clear();
    }

    public static Function<Integer, String> describer() {
        return null;
    }

    Runnable flusher() {
        return this::flush;
    }

    private void flush() {
    }

    private static String describe(String prefix, int count) {
        return prefix + count;
    }

    public static void main(String[] args) {
        Channels access = new Channels();
        access.add("music");
        access.clear();
        Function<Integer, String> describer = describer();
        System.out.println(describer == null ? "no describer" : describer.apply(3));
        access.executor.shutdown();
    }
}
