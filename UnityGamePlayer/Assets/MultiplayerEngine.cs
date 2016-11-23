using UnityEngine;
using System.Collections;
using WebSockets;

public class MultiplayerEngine : MonoBehaviour {

	// Use this for initialization
	void Start () {
		var server = new Server (8989);

		server.OnConnect (delegate(Client client) {

			client.OnMessage(delegate(string message) {
				client.Send("Got your message:" + message);
			});
		});	
	}
	
	// Update is called once per frame
	void Update () {
	
	}
}
