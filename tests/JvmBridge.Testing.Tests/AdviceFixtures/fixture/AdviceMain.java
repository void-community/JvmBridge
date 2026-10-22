package fixture;

import java.lang.reflect.Method;
import java.lang.reflect.Modifier;

public class AdviceMain {
    public static void main(String[] arguments) throws Exception {
        AdviceSubject subject = new AdviceSubject();
        StringBuilder builder = new StringBuilder();

        System.out.println("staticInt=" + AdviceSubject.staticInt(3, 4L));
        System.out.println("staticLong=" + AdviceSubject.staticLong(2.5, "abc"));
        System.out.println("staticDouble=" + AdviceSubject.staticDouble(1.5f, new int[] {1, 2}, "o"));
        AdviceSubject.staticVoid(builder);
        System.out.println("staticVoid=" + builder);
        System.out.println("staticString=" + AdviceSubject.staticString("x"));
        System.out.println("staticArray=" + AdviceSubject.staticArray(3).length);
        System.out.println("instanceInt=" + subject.instanceInt(1));
        System.out.println("instanceLong=" + subject.instanceLong(10L, 20L));
        System.out.println("instanceString=" + subject.instanceString("s", 'c', true, (byte) 1, (short) 2));
        subject.instanceVoid("arg");
        System.out.println("syncMethod=" + subject.syncMethod(21));
        System.out.println("withTry=" + subject.withTry(5) + "," + subject.withTry(-5));
        System.out.println("annotated=" + subject.annotated(9) + " deprecated=" + AdviceSubject.class.getMethod("annotated", int.class).isAnnotationPresent(Deprecated.class));

        boolean hasBody = false;
        for (Method method : AdviceSubject.class.getDeclaredMethods()) {
            if (method.getName().equals("annotated$jvmbridge$body") && method.isSynthetic() && Modifier.isPrivate(method.getModifiers())) hasBody = true;
        }
        System.out.println("hasBody=" + hasBody);

        try {
            subject.thrower(4);
        } catch (IllegalArgumentException failure) {
            StackTraceElement[] frames = failure.getStackTrace();
            System.out.println("thrower=" + failure.getMessage() + " frames=" + frames[0].getMethodName() + "," + frames[1].getMethodName());
        }

        for (String line : Log.lines) System.out.println("log " + line);
    }
}
