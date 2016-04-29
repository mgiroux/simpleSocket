using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Text;
using System.Timers;

public interface ISocketHandler
{
	void socketDidConnect(string socketId);
	void socketDidDisconnect(string socketId);
	void socketDidReceiveDataAsByteArray(byte[] data);
	void socketDidReceiveDataAsString(string data);
}

public class Sockets
{
	public const bool AutoReconnect = true;

	// Message handling
	public StringBuilder message = StringBuilder();

	// Socket and connection status
	public bool connected = false;
	public Socket socket;

	public string remoteIP;
	public int remotePort;
	public string socketName;

	// End of line delimiter
	public string endOfTransmission = "<EOF>";

	// Event delegate
	public ISocketHandler eventHandler;

	// Handle Disconnections timer
	public static System.Timers.Timer statusTimer;
		
	// State handler
	class StateObject
	{
		internal byte[] sBuffer;
		internal Socket sSocket;

		internal StateObject(int size, Socket sock)
		{
			sBuffer = new byte[size];
			sSocket = sock;
		}
	}

	// Connect
	public void Connect(string ip, int port)
	{
		IPAddress _ip       = IPAddress.Parse(ip);
		IPEndPoint remoteEP = new IPEndPoint(_ip, port);

		remoteIP   = ip;
		remotePort = port;
		socketName = ip + port;

		Socket clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
		socket = clientSocket;

		clientSocket.BeginConnect(remoteEP, new AsyncCallback (this.ConnectCallback), clientSocket);
	}

	// Disconnect now!
	public void Disconnect()
	{
		statusTimer = null;

		try {
			socket.Shutdown(SocketShutdown.Both);
			socket.Close();
		} catch (SocketException e) {
			// Don't really handle, the socket was not connected anyway
			Console.WriteLine(e);
		}
	}

	// Connected or not
	public void ConnectCallback(IAsyncResult asyncConnect)
	{
		Socket clientSocket = (Socket)asyncConnect.AsyncState;
		clientSocket.EndConnect (asyncConnect);

		if (!clientSocket.Connected) {
			Console.WriteLine("-> client is not connected.");
			connected = false;
			return;
		} else {
			Console.WriteLine("-> client is connected.");
			connected = true;
		}

		StateObject stateObject = new StateObject(1, clientSocket);
		clientSocket.BeginReceive(stateObject.sBuffer, 0, stateObject.sBuffer.Length, SocketFlags.None, new AsyncCallback (this.ReceiveCallback), stateObject);

		statusTimer          = new System.Timers.Timer();
		statusTimer.Elapsed += new ElapsedEventHandler(CheckForStatus);
		statusTimer.Interval = 500;
		statusTimer.Enabled  = true;

		// Delegate Callback
		eventHandler.socketDidConnect(socketName);
	}
		
	// Write to socket
	public void Write(string message)
	{
		if (connected) {
			byte[] messageBytes     = Encoding.UTF8.GetBytes(message);
			StateObject stateObject = new StateObject(1, socket);

			Console.WriteLine("-> Writing to socket");
			stateObject.sSocket.Send (messageBytes);
		}
	}

	// Private Methods

	private void DisconnectCallback()
	{
		Console.WriteLine("-> Connection was lost");
		Disconnect ();

		// Delegate Callback
		eventHandler.socketDidDisconnect(socketName);
	}

	// Received data
	private void ReceiveCallback(IAsyncResult asyncReceive)
	{
		StateObject stateObject = (StateObject)asyncReceive.AsyncState;

		message.Append(Encoding.ASCII.GetString (stateObject.sBuffer));

		if (message.ToString().Contains(endOfTransmission)) {
			message = StringBuilder();

			// Delegate Callback
			eventHandler.socketDidReceiveDataAsByteArray(stateObject.sBuffer);
			eventHandler.socketDidReceiveDataAsString(message);
		}

		socket.BeginReceive (stateObject.sBuffer, 0, stateObject.sBuffer.Length, SocketFlags.None, new AsyncCallback (this.ReceiveCallback), stateObject);
	}

	// Check if socket is active
	private void CheckForStatus(object source, ElapsedEventArgs e)
	{
		// Detect if client disconnected
		if (socket.Poll(0, SelectMode.SelectRead)) {
			byte[] buff = new byte[1];

			if (socket.Receive(buff, SocketFlags.Peek) == 0) {
				DisconnectCallback();
				connected = false;

				statusTimer.Enabled = false;
				statusTimer 		= null;

				if (AutoReconnect) {
					Console.WriteLine("-> Trying to reconnect");
					Connect(remoteIP, remotePort);
				}
			} else {
				connected = true;
			}
		} else {
			connected = true;
		}
	}
}