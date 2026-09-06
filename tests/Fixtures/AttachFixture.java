import com.sun.tools.attach.VirtualMachine;
public final class AttachFixture {
    public static void main(String[] arguments) throws Exception {
        VirtualMachine machine = VirtualMachine.attach(arguments[0]);
        try { machine.loadAgentPath(arguments[1]); }
        finally { machine.detach(); }
    }
}
