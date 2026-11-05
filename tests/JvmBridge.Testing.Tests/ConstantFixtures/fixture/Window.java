package fixture;

import java.util.concurrent.TimeUnit;

// Shaped like the early display window of Forge and NeoForge: initWindow's watchdog waits 10 seconds, and the render loop's minimum frame time is 10 milliseconds, the same long
// constant in another method.
public class Window {
    private static final long MINFRAMETIME = TimeUnit.MILLISECONDS.toNanos(10L);

    static void initWindow(String title) {
        System.out.println(title);
        System.out.println(10L);
        System.out.println(100000);
        System.out.println("Timed out");
    }

    static void render() {
        System.out.println(MINFRAMETIME);
        System.out.println(100000);
        System.out.println("Timed out");
    }

    public static void main(String[] args) {
        initWindow("init");
        render();
    }
}
