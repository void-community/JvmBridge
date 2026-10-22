package fixture;

import java.lang.annotation.Retention;
import java.lang.annotation.RetentionPolicy;
import java.util.AbstractList;

/** Compiled with: javac -source 8 -target 8 -g:none Annotated.java, keeping only Annotated.class. */
@Deprecated
@Annotated.Visible(value = "x", numbers = {1, 2, 3}, type = String.class, policy = RetentionPolicy.SOURCE, nested = @Annotated.Hidden)
@Annotated.Hidden
public class Annotated extends AbstractList<String> implements java.io.Serializable, Runnable {
    @Retention(RetentionPolicy.RUNTIME)
    public @interface Visible {
        String value();
        int[] numbers() default {};
        Class<?> type() default Object.class;
        RetentionPolicy policy() default RetentionPolicy.CLASS;
        Hidden nested() default @Hidden;
    }

    @Retention(RetentionPolicy.CLASS)
    public @interface Hidden {
    }

    public String get(int index) { return "item" + index; }
    public int size() { return 0; }
    public void run() { }
}
