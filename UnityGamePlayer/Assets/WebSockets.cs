using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Net;
using System.Threading;
using System.Text.RegularExpressions;
using System.Text;
using System;

namespace WebSockets{
	/* References: 
	 * http://tools.ietf.org/html/draft-ietf-hybi-thewebsocketprotocol-17 
	 * https://developer.mozilla.org/en-US/docs/Web/API/WebSockets_API/Writing_WebSocket_server
	 * http://stackoverflow.com/questions/37547408/c-sharp-sending-a-websocket-message-to-the-client
	 * http://stackoverflow.com/questions/8261407/simple-c-sharp-websockets-server
	 * http://stackoverflow.com/questions/14102862/websocket-is-not-able-to-send-large-data
	 * http://stackoverflow.com/questions/3919804/how-do-i-convert-an-int-to-two-bytes-in-c
	 */
	public class Client {
		private TcpClient client;
		private NetworkStream stream;
		private bool upgraded = false;
		private Thread recieveThread = null;
		private bool recieving = true;
		private bool debug = true;
		private List<Action<string>> onMessageCallbacks = new List<Action<string>>();

		public bool HasUpgraded(){
			return upgraded;
		}

		public void OnMessage(Action<string> callback){
			onMessageCallbacks.Add (callback);
		}

		public void Send(string message){
			EncodeAndSendMessage (message);
		}

		private void Log(string message){
			if (debug) {
				// [ Uncomment this line for unity: ]
				UnityEngine.Debug.Log (message);

				// [ Uncomment this line for console application: ]
				// System.Console.WriteLine (message);
			}
		}

		public Client(TcpClient client){
			this.client = client;
			stream = client.GetStream ();

			recieveThread = new Thread (new ThreadStart (RecieveThread));
			recieveThread.Start ();
		}

		void RecieveThread(){
			Log ("Entered recieve thread");
			while (recieving) {
				Log ("Entered recieve loop");

				while (!stream.DataAvailable) {
					// [ Don't read anymore if ending thread ]
					if (!recieving)
						break;
				}

				// [ Don't read anymore if ending thread ]
				if (!recieving)
					break;

				Log ("Waiting for message");

				// [ Get data ]
				Byte[] bytes = new Byte[client.Available];
				stream.Read(bytes, 0, bytes.Length);

				Log ("Read Message");


				// [ Check if websocket wants to upgrade ]
				if (!upgraded) {
					Log ("Upgrading...");
					if (UpgradeToWebSocket (bytes, stream)) {
						Log ("Upgraded Protocols");
						upgraded = true;

						continue;
					}
				}

				// [ Skip rest of loop if haven't upgraded yet ]
				if (!upgraded) continue;

				Log ("Decoding message...");

				// [ Decode Message ]
				string data = DecodeMessage(bytes);

				Log ("Decoded Message...");

				// [ Call any callbacks with message ]
				foreach (var callback in onMessageCallbacks) {
					callback (data);
				}
			}
		}

		private bool UpgradeToWebSocket(Byte[] bytes, NetworkStream stream){
			string data = Encoding.UTF8.GetString(bytes);

			if (new Regex("^GET").IsMatch(data)) {
				Byte[] response = Encoding.UTF8.GetBytes("HTTP/1.1 101 Switching Protocols" + Environment.NewLine
					+ "Connection: Upgrade" + Environment.NewLine
					+ "Upgrade: websocket" + Environment.NewLine
					+ "Sec-WebSocket-Accept: " + Convert.ToBase64String (
						System.Security.Cryptography.SHA1.Create().ComputeHash (
							Encoding.UTF8.GetBytes (
								new Regex("Sec-WebSocket-Key: (.*)").Match(data).Groups[1].Value.Trim() + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"
							)
						)
					) + Environment.NewLine
					+ Environment.NewLine);

				stream.Write(response, 0, response.Length);

				return true;
			}

			return false;
		}

		private string DecodeMessage (Byte[] bytes){
			string DETEXT = "";
			if (bytes[0] == 129)
			{
				int position = 0;
				int Type = 0;
				ulong length = 0;
				if (bytes[1] - 128 >= 0 && bytes[1] - 128 <= 125)
				{
					length = (ulong)bytes[1] - 128;
					position = 2;
				}
				else if (bytes[1] - 128 == 126)
				{
					Type = 1;
					length = (ulong)256 * bytes[2] + bytes[3];
					position = 4;
				}
				else if (bytes[1] - 128 == 127)
				{
					Type = 2;
					for (int i = 0; i < 8; i++)
					{
						ulong pow = Convert.ToUInt64(Math.Pow(256, (7 - i)));
						length = length + bytes[2 + i] * pow;
						position = 10;
					}
				}
				else
				{
					Type = 3;
				}

				if (Type < 3)
				{
					Byte[] key = new Byte[4] { bytes[position], bytes[position + 1], bytes[position + 2], bytes[position + 3] };
					Byte[] decoded = new Byte[bytes.Length - (4 + position)];
					Byte[] encoded = new Byte[bytes.Length - (4 + position)];

					for (long i = 0; i < bytes.Length - (4 + position); i++) encoded[i] = bytes[i + position + 4];

					for (int i = 0; i < encoded.Length; i++) decoded[i] = (Byte)(encoded[i] ^ key[i % 4]);

					DETEXT = Encoding.UTF8.GetString(decoded);
				}
			}
			else
			{
				Log("error 2: " + bytes[0].ToString());
			}
			return DETEXT;
		}

		private void EncodeAndSendMessage(string message){
			List<byte> bytes = new List<byte>();
			// see http://tools.ietf.org/html/draft-ietf-hybi-thewebsocketprotocol-17 
			//     page 30 for this:
			// 0                   1                   2                   3
			// 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
			// +-+-+-+-+-------+-+-------------+-------------------------------+
			// |F|R|R|R| opcode|M| Payload len |    Extended payload length    |
			// |I|S|S|S|  (4)  |A|     (7)     |             (16/64)           |
			// |N|V|V|V|       |S|             |   (if payload len==126/127)   |
			// | |1|2|3|       |K|             |                               |
			// +-+-+-+-+-------+-+-------------+ - - - - - - - - - - - - - - - +
			// |     Extended payload length continued, if payload len == 127  |
			// + - - - - - - - - - - - - - - - +-------------------------------+
			// |                               |Masking-key, if MASK set to 1  |
			// +-------------------------------+-------------------------------+
			// | Masking-key (continued)       |          Payload Data         |
			// +-------------------------------- - - - - - - - - - - - - - - - +
			// :                     Payload Data continued ...                :
			// + - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - +
			// |                     Payload Data continued ...                |
			// +---------------------------------------------------------------+
			//
			//0x81 = 10000001 which says according to the table above:
			//       1        it's the final message 
			//        000     RSV1-3 are extensions which must be negotiated first
			//           0001 opcode %x1 denotes a text frame
			bytes.Add(0x81);
			//0x04 = 00001000
			//       0        No mask
			//       0001000 Rest of the 7 bytes left is the length of the payload.
			// bytes.Add(0x04);
			if (message.Length <= 125) {
				bytes.Add ((Byte)message.Length);
			} else if (message.Length <= 65535) {
				bytes.Add (126);

				byte b0 = (byte)message.Length,
				b1 = (byte)(message.Length >> 8);

				bytes.Add (b1);
				bytes.Add (b0);
			} else {
				throw new ArgumentException ("Message is too long");
			}

			// add the payload
			bytes.AddRange(Encoding.UTF8.GetBytes(message));

			//write it!
			if (message.Length <= 125) {
				stream.Write (bytes.ToArray(), 0, message.Length + 2);
			} else {
				stream.Write (bytes.ToArray(), 0, message.Length + 4);
			}

		}


	}

	public class Server {
		private TcpListener server;
		private List<Client> clients = new List<Client>();

		private Thread 		acceptThread = null;
		private bool 		debug = true;
		private string 		ip = "";
		private int 		port = 0;

		private List<Action<Client>> onConnectCallbacks = new List<Action<Client>>();

		public Server(int port=80){
			this.ip = GetLocalIPAddress();
			UnityEngine.Debug.Log (this.ip);
			this.port = port;

			// [ Accepts in another thread so main thread isn't blocked ]
			acceptThread = new Thread (new ThreadStart (AcceptThread));
			acceptThread.Start ();
		}

		public Server(string ip,int port=80){
			this.ip = ip;
			this.port = port;

			// [ Accepts in another thread so main thread isn't blocked ]
			acceptThread = new Thread (new ThreadStart (AcceptThread));
			acceptThread.Start ();
		}

		public static string GetLocalIPAddress()
		{
			var host = Dns.GetHostEntry(Dns.GetHostName());
			foreach (var ip in host.AddressList)
			{
				if (ip.AddressFamily == AddressFamily.InterNetwork)
				{
					return ip.ToString();
				}
			}
			throw new Exception("Local IP Address Not Found!");
		}

		private void Log(string message){
			if (debug) {
				// [ Uncomment this line for unity: ]
				UnityEngine.Debug.Log (message);

				// [ Uncomment this line for console application: ]
				// System.Console.WriteLine (message);
			}
		}

		public void OnConnect(Action<Client> callback){
			onConnectCallbacks.Add (callback);
		}

		private void AcceptThread(){
			// [ Creates the server ]
			server = new TcpListener(IPAddress.Parse(this.ip), this.port);
			server.Start();
			Log("Server has started on " + this.ip + ":" + this.port + ". Waiting for a connection...");

			// [ Loops accept ]
			while (true) {
				// [ Accepts client (blocks while waiting for client) ]
				TcpClient tcpClient = server.AcceptTcpClient();
				Client client = new Client (tcpClient);
				while (!client.HasUpgraded()) {

				}

				foreach (var callback in onConnectCallbacks) {
					callback (client);
				}

				// [ Add client to array of clients ]
				clients.Add(client);					
			}
		}
	}
}
