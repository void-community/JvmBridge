package jvmbridge;

import java.io.File;
import java.io.IOException;
import java.io.InputStream;
import java.io.FileOutputStream;
import java.io.OutputStream;
import java.util.Properties;

/**
 * Starts a JvmBridge agent from a jar, so that {@code -javaagent:agent.jar=options} works. The jar holds one native
 * library per runtime identifier and an index, {@code native.properties}, that maps each identifier to the name of its
 * library, in a directory named after this class. This class extracts the library of the running platform to a private
 * temporary directory and loads it with {@link System#load(String)}; the library starts its agent from
 * {@code JNI_OnLoad} and reads its options from the system property {@code jvmbridge.options}.
 *
 * {@code jvmbridge pack} gives this class a name of its own in every jar (a name with the same length, so only one
 * constant changes), because the JVM loads the class of the first {@code -javaagent} jar for every jar that names it.
 *
 * It uses only Java 6 APIs. Compile it with a Java 8 compiler and {@code -source 6 -target 6}, so that the class runs on every JVM from Java 6.
 */
public final class Launcher {
    private static final String OPTIONS = "jvmbridge.options";
    private static boolean started;

    private Launcher() {
    }

    public static void premain(String arguments) {
        start(arguments);
    }

    public static void agentmain(String arguments) {
        start(arguments);
    }

    private static String directory() {
        return "/" + Launcher.class.getName().replace('.', '/') + "/";
    }

    static String platform() {
        String system = System.getProperty("os.name", "").toLowerCase();
        String architecture = System.getProperty("os.arch", "").toLowerCase();
        String cpu;

        if (architecture.equals("amd64") || architecture.equals("x86_64")) {
            cpu = "x64";
        } else if (architecture.equals("aarch64") || architecture.equals("arm64")) {
            cpu = "arm64";
        } else if (architecture.equals("x86") || architecture.equals("i386") || architecture.equals("i686")) {
            cpu = "x86";
        } else if (architecture.startsWith("arm")) {
            cpu = "arm";
        } else {
            throw new IllegalStateException("JvmBridge does not support the processor architecture " + architecture);
        }

        if (system.startsWith("windows")) {
            return "win-" + cpu;
        }

        if (system.startsWith("mac")) {
            return "osx-" + cpu;
        }

        if (system.startsWith("linux")) {
            return (isMusl() ? "linux-musl-" : "linux-") + cpu;
        }

        throw new IllegalStateException("JvmBridge does not support the operating system " + system);
    }

    private static boolean isMusl() {
        String[] names = new File("/lib").list();

        if (names == null) {
            return false;
        }

        for (int index = 0; index < names.length; index++) {
            if (names[index].startsWith("ld-musl-")) {
                return true;
            }
        }

        return false;
    }

    private static void restrictToOwner(File directory) throws IOException {
        boolean closed = directory.setReadable(false, false) && directory.setWritable(false, false) && directory.setExecutable(false, false);
        boolean opened = directory.setReadable(true, true) && directory.setWritable(true, true) && directory.setExecutable(true, true);

        if (!closed || !opened) {
            throw new IOException("Could not make " + directory + " private");
        }
    }

    private static synchronized void start(String arguments) {
        if (started) {
            System.err.println("jvmbridge: " + Launcher.class.getName() + " is already started in this JVM; ignoring the second start.");
            return;
        }

        try {
            String platform = platform();
            String indexName = directory() + "native.properties";
            Properties index = new Properties();
            InputStream list = Launcher.class.getResourceAsStream(indexName);

            if (list == null) {
                throw new IllegalStateException("The jar has no " + indexName + ". Build it with 'jvmbridge pack'.");
            }

            try {
                index.load(list);
            } finally {
                list.close();
            }

            String name = index.getProperty(platform);

            if (name == null) {
                throw new IllegalStateException("The jar has no agent library for " + platform + ". It has: " + index.stringPropertyNames());
            }

            System.setProperty(OPTIONS, arguments == null ? "" : arguments);
            File library = extract(directory() + "native/" + platform + "/" + name, name);

            System.load(library.getAbsolutePath());

            // Unix keeps a loaded library mapped after its file is gone, so no copy is left behind even if the JVM crashes. Windows
            // refuses to delete a loaded library; deleteOnExit removes it when the JVM exits.
            library.delete();
            library.getParentFile().delete();
            started = true;
        } catch (IOException failure) {
            throw new IllegalStateException("JvmBridge could not extract its agent library: " + failure, failure);
        }
    }

    private static File extract(String resource, String name) throws IOException {
        InputStream source = Launcher.class.getResourceAsStream(resource);

        if (source == null) {
            throw new IllegalStateException("The jar is missing " + resource);
        }

        // A private directory per start: a predictable path in a shared temporary directory would let another user supply the library. mkdir fails when
        // someone else created the directory between delete and mkdir, and the directory is closed to everyone else before the library goes in.
        File directory = File.createTempFile("jvmbridge-", "");

        if (!directory.delete() || !directory.mkdir()) {
            throw new IOException("Could not create the private directory " + directory);
        }

        restrictToOwner(directory);

        File library = new File(directory, name);

        directory.deleteOnExit();
        library.deleteOnExit();

        try {
            OutputStream target = new FileOutputStream(library);

            try {
                byte[] buffer = new byte[65536];

                for (int count = source.read(buffer); count > 0; count = source.read(buffer)) {
                    target.write(buffer, 0, count);
                }
            } finally {
                target.close();
            }
        } finally {
            source.close();
        }

        return library;
    }
}
