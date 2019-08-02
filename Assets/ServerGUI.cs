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
        else if (args[1] == "Saea")
        {
            ushort port = ushort.Parse(args[2]);

            StartSaeaServer(port);
        }
        else if (IsHeadless())
        {
            usage(args);
        }

        lastTime = Time.time;
        framecount = Time.frameCount;
        InvokeRepeating(nameof(ShowFps), 10f, 10f);
    }

    float lastTime;
    int framecount;

    public void ShowFps()
    {
        float now = Time.time;
        int newCount = Time.frameCount;

        float fps = (newCount - framecount) / (now - lastTime);

        Debug.Log("FPS " + fps);

        framecount = newCount;
        lastTime = now;
    }


    private static void usage(string[] args)
    {
        Console.WriteLine("Usage:");
        Console.WriteLine($"\t{args[0]} Telepathy <port>   To start Telepathy server");
        Console.WriteLine($"\t{args[0]} Async <port>       To start Async TCP server");
        Console.WriteLine($"\t{args[0]} Saea <port>       To start Saea TCP server");

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

            if (GUILayout.Button("Start Saea Server"))
            {
                StartSaeaServer(ushort.Parse(port));
            }
        }
    }

    #region echo server using Async Tcp
    Mirror.Tcp.Server asyncServer;

    private void StartAsyncServer(ushort port)
    {
        asyncServer = new Mirror.Tcp.Server();

        asyncServer.ReceivedData += (connId, data) =>
        {
            asyncServer.Send(connId, new ArraySegment<byte>(data));
        };


        _ = asyncServer.ListenAsync(port);
        started = true;

        Debug.Log($"Async Tcp started at port {port}");
    }

    private void Server_ReceivedData(int arg1, byte[] arg2)
    {
        throw new NotImplementedException();
    }
    #endregion

    #region echo server using Async Tcp
    Mirror.Saea.Server saeaServer;

    private void StartSaeaServer(ushort port)
    {
        saeaServer = new Mirror.Saea.Server();

        saeaServer.Start(port);
        started = true;

        Debug.Log($"Async Tcp started at port {port}");
    }
    #endregion

    #region echo server in Telepathy

    Telepathy.Server telepathyServer;

    private void StartTelepathyServer(ushort port)
    {
        telepathyServer = new Telepathy.Server();
        telepathyServer.Start(port);
        started = true;

        Debug.Log($"Telepathy started at port {port}");
    }

    private void Update()
    {
        if (telepathyServer != null)
        {

            while (telepathyServer.GetNextMessage(out Telepathy.Message message))
            {

                if (message.eventType == Telepathy.EventType.Data)
                    telepathyServer.Send(message.connectionId, message.data);
            }
        }
        else if (saeaServer != null)
        {
            while (saeaServer.GetNextMessage(out Mirror.Saea.Message message))
            {

                if (message.eventType == Mirror.Saea.EventType.Data)
                    telepathyServer.Send(message.connectionId, message.data);
            }
        }
        else if (asyncServer != null)
        {
            asyncServer.Flush();
        }
    }

    #endregion

}
