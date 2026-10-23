package fixture;

public final class Greeter {
    public static void main(String[] arguments) {
        System.out.println("hello " + System.getProperty("java.specification.version"));
        if (arguments.length > 0 && arguments[0].startsWith("exit")) System.exit(Integer.parseInt(arguments[0].substring(4)));
    }
}
