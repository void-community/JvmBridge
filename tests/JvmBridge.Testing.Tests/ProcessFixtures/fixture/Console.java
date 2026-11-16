package fixture;

import java.io.BufferedReader;
import java.io.File;
import java.io.FileInputStream;
import java.io.InputStreamReader;
import java.lang.management.ManagementFactory;

/**
 * A program that a test drives as it drives a server. "where" prints the working directory and a file read by a relative path; "flood" writes many long lines to both streams; "sleep" waits ten minutes. Without an argument it prints its
 * process id and "ready", then answers each line of its input until "stop", and "child" starts a JVM that sleeps and prints that one's id.
 */
public final class Console {
    private Console() {
    }

    public static void main(String[] arguments) throws Exception {
        String mode = arguments.length > 0 ? arguments[0] : "console";
        if (mode.equals("where")) {
            System.out.println("DIR=" + System.getProperty("user.dir"));
            System.out.println("FILE=" + read(new File("relative.txt")));
        } else if (mode.equals("flood")) {
            flood(Integer.parseInt(arguments[1]));
        } else if (mode.equals("sleep")) {
            Thread.sleep(600000L);
        } else {
            answer();
        }
    }

    private static void answer() throws Exception {
        System.out.println("first");
        System.out.println("second");
        System.out.println("PID=" + ManagementFactory.getRuntimeMXBean().getName().split("@")[0]);
        System.out.println("ready");
        BufferedReader input = new BufferedReader(new InputStreamReader(System.in, "UTF-8"));
        String line;
        while ((line = input.readLine()) != null) {
            if (line.equals("stop")) {
                System.out.println("stopping");
                return;
            }
            if (line.equals("child")) {
                String java = System.getProperty("java.home") + File.separator + "bin" + File.separator + "java";
                Process child = new ProcessBuilder(java, "-cp", System.getProperty("java.class.path"), "fixture.Console", "sleep").start();
                // Process.pid() is Java 9 and later.
                System.out.println("CHILD=" + Process.class.getMethod("pid").invoke(child));
                continue;
            }
            System.out.println("echo: " + line);
        }
        System.out.println("eof");
        System.exit(3);
    }

    private static void flood(int count) {
        StringBuilder padding = new StringBuilder();
        for (int index = 0; index < 64; index++) {
            padding.append('x');
        }
        for (int index = 0; index < count; index++) {
            if (index % 2 == 0) {
                System.out.println("out " + index + " " + padding);
            } else {
                System.err.println("err " + index + " " + padding);
            }
        }
        System.out.println("flood done");
    }

    private static String read(File file) throws Exception {
        FileInputStream stream = new FileInputStream(file);
        try {
            byte[] buffer = new byte[(int) file.length()];
            int length = stream.read(buffer);
            return new String(buffer, 0, Math.max(length, 0), "UTF-8").trim();
        } finally {
            stream.close();
        }
    }
}
