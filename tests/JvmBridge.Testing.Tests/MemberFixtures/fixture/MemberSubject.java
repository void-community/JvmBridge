package fixture;

/** Private members that a patch adds a getter, a setter and invokers for, called by reflection once the class is patched; in the Java 6 language. */
public final class MemberSubject {
    private static int count = 7;

    private String name = "hidden";

    private String secret(int times) {
        StringBuilder text = new StringBuilder();
        for (int index = 0; index < times; index++) {
            text.append(name);
        }
        return text.toString();
    }

    private static int twice(int value) {
        return value * 2;
    }

    public static void main(String[] arguments) throws Exception {
        MemberSubject subject = new MemberSubject();
        Class<?> type = MemberSubject.class;
        Object read = type.getMethod("agent$name").invoke(subject);
        type.getMethod("agent$setName", Object.class).invoke(subject, "changed");
        Object counted = type.getMethod("agent$count").invoke(null);
        Object secret = type.getMethod("agent$secret", int.class).invoke(subject, 2);
        Object doubled = type.getMethod("agent$twice", int.class).invoke(null, 21);
        System.out.println("NAME=" + read + " COUNT=" + counted + " SECRET=" + secret + " TWICE=" + doubled);
    }
}
