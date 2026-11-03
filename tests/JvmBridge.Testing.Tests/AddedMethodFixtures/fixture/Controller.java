package fixture;

// Shaped like Minecraft 1.7's PlayerControllerMP.updateController: the received packets are handled only while the connection is open, so a chat that came just before
// the disconnect is never shown.
public class Controller {
    private final Connection connection;

    public Controller(Connection connection) {
        this.connection = connection;
    }

    public void updateController() {
        if (connection.isChannelOpen()) {
            connection.processReceivedPackets();
        } else {
            System.out.println("disconnected");
        }
    }

    public static void main(String[] args) {
        Connection connection = new Connection();
        Controller controller = new Controller(connection);

        connection.receive("hello");
        controller.updateController();
        connection.receive("goodbye");
        connection.close();
        controller.updateController();
    }
}
