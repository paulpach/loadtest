using System.Collections.Concurrent;
using System.Net.Sockets;
using UnityEngine;

namespace Mirror.Saea
{
    public abstract class Common
    {
        // nagle: disabled by default
        public bool NoDelay = true;

        // the big buffer. static for maximum performance, so we can use one big
        // buffer even if we run 1k clients in one process.
        // (having it static also fixes our loadtest memory leak)
        protected static BigBuffer bigBuffer = new BigBuffer();

        protected abstract void ProcessReceive(SocketAsyncEventArgs e);
        protected abstract void ProcessSend(SocketAsyncEventArgs e);

        protected void IO_Completed(object sender, SocketAsyncEventArgs e)
        {
            // determine which type of operation just completed and call the associated handler
            switch (e.LastOperation)
            {
                case SocketAsyncOperation.Receive:
                    ProcessReceive(e);
                    break;

                case SocketAsyncOperation.Send:
                    ProcessSend(e);
                    break;

                default:
                    Debug.LogError("The last operation completed on the socket was not a receive or send");
                    break;
            }
        }
    }
}