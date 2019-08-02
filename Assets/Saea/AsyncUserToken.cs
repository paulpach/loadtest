using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace Mirror.Saea
{
    public class AsyncUserToken
    {
        public IPAddress IpAddress;
        public EndPoint Remote;
        public Socket Socket;
        public DateTime ConnectTime;
        public string UserInfo;
        public int connectionId;
        // size of the incoming message
        public int messageLength;
        public int bytesReceived;

        //public byte[] header = new byte[4]; // buffered so we don't allocate it all the time
        public MemoryStream buffer = new MemoryStream();
    }
}