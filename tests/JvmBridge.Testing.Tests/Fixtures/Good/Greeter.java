package fixture;

public final class Greeter {
    public static void main(String[] arguments) throws Exception {
        System.out.println("hello " + String.join(",", arguments));
        if (arguments.length > 0 && arguments[0].equals("sleep")) Thread.sleep(60000);
        if (arguments.length > 0 && arguments[0].startsWith("exit")) System.exit(Integer.parseInt(arguments[0].substring(4)));
    }
}
