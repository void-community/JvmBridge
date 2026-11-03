package fixture;

import java.util.ArrayList;
import java.util.List;

public class Connection {
    private final List<String> queue = new ArrayList<String>();
    private boolean open = true;

    public boolean isChannelOpen() {
        return open;
    }

    public void receive(String message) {
        queue.add(message);
    }

    public void close() {
        open = false;
    }

    public void processReceivedPackets() {
        for (String message : queue) {
            System.out.println("chat " + message);
        }

        queue.clear();
    }
}
