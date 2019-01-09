using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;


public class ServerGUI : MonoBehaviour {

	// Use this for initialization
	void Start () {
        Application.targetFrameRate = 60;

        string[] args = System.Environment.GetCommandLineArgs();

        if (args.Length <=1 )
        {
            usage(args);
        }
        else if (args[1] == "Telepathy")
        {
            ushort port = ushort.Parse(args[2]);

            StartTelepathyServer(port);
        }
        else if (args[1] == "Async")
        {
            ushort port = ushort.Parse(args[2]);

            StartAsyncServer(port);
        }
        else if (IsHeadless())
        {
            usage(args);
        }
    }

    private static void usage(string[] args)
    {
        Console.WriteLine("Usage:");
        Console.WriteLine($"\t{args[0]} Telepathy <port>   To start Telepathy server");
        Console.WriteLine($"\t{args[0]} Async <port>       To start Async TCP server");

        Application.Quit();
    }

    bool IsHeadless()
    {
        return SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;
    }

    public bool started = false;

    string port = "7777";

    void OnGUI()
    {

        if (started)
        {
            GUILayout.Label($"Server started at port {port}");
        }
        else
        {
            port = GUILayout.TextField(port);

            if (GUILayout.Button("Start Telepathy"))
            {
                StartTelepathyServer(ushort.Parse(port));
            }

            if (GUILayout.Button("Start Async Server"))
            {
                StartAsyncServer(ushort.Parse(port));
            }
        }
    }

    #region echo server using Async Tcp
    private void StartAsyncServer(ushort port)
    {
        Mirror.Transport.Tcp.Server server = new Mirror.Transport.Tcp.Server();

        server.ReceivedData += server.Send;


        server.Listen(port);
        started = true;

        Debug.Log($"Async Tcp started at port {port}");
    }
    #endregion

    #region echo server in Telepathy

    Telepathy.Server server;

    private void StartTelepathyServer(ushort port)
    {
        server = new Telepathy.Server();
        server.Start(port);
        started = true;

        Debug.Log($"Telepathy started at port {port}");
    }

    private void Update()
    {
        if (server == null)
            return;

        Telepathy.Message message;

        while (server.GetNextMessage(out message))
        {

            if (message.eventType == Telepathy.EventType.Data)
                server.Send(message.connectionId, message.data);
        }
    }

    #endregion

}
