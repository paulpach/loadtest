using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace Mirror.Saea
{
    public class Server : Common
    {
        Socket listenSocket;

        // Dict<connId, token>
        readonly ConcurrentDictionary<int, AsyncUserToken> clients = new ConcurrentDictionary<int, AsyncUserToken>();

        internal readonly ConcurrentQueue<Message> messageQueue = new ConcurrentQueue<Message>();

        // connectionId counter
        // (right now we only use it from one listener thread, but we might have
        //  multiple threads later in case of WebSockets etc.)
        // -> static so that another server instance doesn't start at 0 again.
        int counter;

        public bool Active { get; set; }

        // public next id function in case someone needs to reserve an id
        // (e.g. if hostMode should always have 0 connection and external
        //  connections should start at 1, etc.)
        private int NextConnectionId()
        {
            int id = Interlocked.Increment(ref counter);

            // it's very unlikely that we reach the uint limit of 2 billion.
            // even with 1 new connection per second, this would take 68 years.
            // -> but if it happens, then we should throw an exception because
            //    the caller probably should stop accepting clients.
            // -> it's hardly worth using 'bool Next(out id)' for that case
            //    because it's just so unlikely.
            if (id == int.MaxValue)
            {
                throw new Exception("connection id limit reached: " + id);
            }

            return id;
        }

        public bool Start(int port)
        {
            try
            {
                clients.Clear();
                listenSocket = new Socket(SocketType.Stream, ProtocolType.Tcp)
                {
                    NoDelay = NoDelay
                };
                IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Any, port);
                listenSocket.Bind(localEndPoint);

                // start the server with a listen backlog of 100 connections
                listenSocket.Listen(100);

                this.Active = true;
                // post accepts on the listening socket
                StartAccept(null);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("Server.Start failed: " + e);
                return false;
            }
        }

        public void Stop()
        {
            foreach (KeyValuePair<int, AsyncUserToken> kvp in clients)
            {
                AsyncUserToken token = kvp.Value;
                try
                {
                    token.Socket.Shutdown(SocketShutdown.Both);
                    OnClientDisconnected(token);
                }
                catch (Exception) { }
            }

            try
            {
                listenSocket?.Shutdown(SocketShutdown.Both);
            }
            catch (Exception) { }

            listenSocket?.Close();
            this.Active = false;

            clients.Clear();
        }

        public bool GetNextMessage(out Message message)
        {
            return messageQueue.TryDequeue(out message);
        }

        public void Disconnect(int connectionId)
        {
            if (clients.TryGetValue(connectionId, out AsyncUserToken token))
            {
                try
                {
                    token.Socket.Shutdown(SocketShutdown.Both);
                }
                catch (Exception) { }
            }
        }

        internal string GetClientAddress(int connectionId)
        {
            if (clients.TryGetValue(connectionId, out AsyncUserToken token))
            {
                return token.IpAddress.ToString();
            }
            return "";
        }

        // Begins an operation to accept a connection request from the client
        //
        // <param name="acceptEventArg">The context object to use when issuing
        // the accept operation on the server's listening socket</param>
        void StartAccept(SocketAsyncEventArgs acceptEventArg)
        {
            if (acceptEventArg == null)
            {
                acceptEventArg = new SocketAsyncEventArgs();
                acceptEventArg.Completed += AcceptEventArg_Completed;
            }
            else
            {
                // socket must be cleared since the context object is being reused
                acceptEventArg.AcceptSocket = null;
            }

            if (!listenSocket.AcceptAsync(acceptEventArg))
            {
                ProcessAccept(acceptEventArg);
            }
        }

        // This method is the callback method associated with Socket.AcceptAsync
        // operations and is invoked when an accept operation is complete
        //
        void AcceptEventArg_Completed(object sender, SocketAsyncEventArgs e)
        {
            ProcessAccept(e);
        }

        void OnClientConnected(AsyncUserToken token)
        {
            messageQueue.Enqueue(new Message(token.connectionId, EventType.Connected, null));
        }

        void OnClientDisconnected(AsyncUserToken token)
        {
            messageQueue.Enqueue(new Message(token.connectionId, EventType.Disconnected, null));
        }

        void ProcessAccept(SocketAsyncEventArgs e)
        {
            if (e.SocketError == SocketError.OperationAborted)
            {
                Stop();
                return;
            }

            try
            {
                // create SocketAsyncEventArgs for this client
                SocketAsyncEventArgs args = new SocketAsyncEventArgs();
                args.Completed += IO_Completed;
                args.UserToken = new AsyncUserToken();

                // assign chunk of big buffer for max performance (see BigBuffer.cs comments)
                if (bigBuffer.Assign(args))
                {
                    // Get the socket for the accepted client connection and put it into the
                    //ReadEventArg object user token
                    AsyncUserToken userToken = (AsyncUserToken) args.UserToken;
                    userToken.Socket = e.AcceptSocket;
                    userToken.ConnectTime = DateTime.Now;
                    userToken.Remote = e.AcceptSocket.RemoteEndPoint;
                    userToken.IpAddress = ((IPEndPoint) (e.AcceptSocket.RemoteEndPoint)).Address;
                    userToken.connectionId = NextConnectionId();

                    clients[userToken.connectionId] = userToken;

                    OnClientConnected(userToken);

                    if (!e.AcceptSocket.ReceiveAsync(args))
                    {
                        ProcessReceive(args);
                    }
                }
                else Debug.LogError("Server.ProcessAccept: failed to assign buffer.");
            }
            catch (Exception exception)
            {
                Debug.LogError("Server.ProcessAccept failed: " + exception);
            }

            StartAccept(e);
        }

        // This method is invoked when an asynchronous receive operation completes.
        // If the remote host closed the connection, then the socket is closed.
        protected override void ProcessReceive(SocketAsyncEventArgs e)
        {
            try
            {
                // check if the remote host closed the connection
                AsyncUserToken token = (AsyncUserToken)e.UserToken;
                if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
                {
                    ArraySegment<byte> data = new ArraySegment<byte>(e.Buffer, e.Offset, e.BytesTransferred);

                   
                    // write it all into our memory stream first
                    token.buffer.Write(e.Buffer, e.Offset, e.BytesTransferred);

                    // keep trying headers (we might need to process >1 message)
                    while (token.buffer.Position >= 4)
                    {
                        // we can read a header. so read it.
                        long bufferSize = token.buffer.Position;
                        token.buffer.Position = 0;
                        byte[] header = new byte[4]; // TODO cache
                        token.buffer.Read(header, 0, header.Length);
                        int contentSize = Utils.BytesToIntBigEndian(header);

                        // avoid -1 attacks from hackers
                        if (contentSize > 0)
                        {
                            // enough content to finish the message?
                            if (bufferSize - token.buffer.Position >= contentSize)
                            {
                                // read content
                                byte[] content = new byte[contentSize];
                                token.buffer.Read(content, 0, content.Length);

                                // process message
                                OnReceiveClientData(token, content);

                                // read what's left in the buffer. this is valid
                                // data that we received at some point. can't lose
                                // it.
                                byte[] remainder = new byte[bufferSize - token.buffer.Position];
                                token.buffer.Read(remainder, 0, remainder.Length);

                                // write it to the beginning of the buffer. this
                                // sets position to the new true end automatically.
                                token.buffer.Position = 0;
                                token.buffer.Write(remainder, 0, remainder.Length);
                            }
                            // otherwise we just need to receive more.
                            else break;
                        }
                        else
                        {
                            CloseClientSocket(e);
                            Debug.LogWarning("Server.ProcessReceive: received negative contentSize: " + contentSize + ". Maybe an attacker tries to exploit the server?");
                        }
                    }
                    

                    // continue receiving
                    if (!token.Socket.ReceiveAsync(e))
                        ProcessReceive(e);
                }
                else
                {
                    Debug.LogWarning("Closing socket " + e.BytesTransferred);
                    CloseClientSocket(e);
                }
            }
            catch (Exception exception)
            {
                CloseClientSocket(e);
                Debug.LogError("Server.ProcessReceive failed: " + exception);
            }
        }

        /*
        private void ProcessData(AsyncUserToken token, ArraySegment<byte> data)
        {
            int offset = 0;

            // have we read the message length?
            for (int i=token.bytesReceived; i< 4; i++)
            {

            }
        }

        private int ReadHeader(AsyncUserToken token, ArraySegment<byte> data, ref int offset)
        {
            while (token.bytesReceived < 4 && offset < data.Count)
            {

            }
        }*/

        // This method is invoked when an asynchronous send operation completes.
        protected override void ProcessSend(SocketAsyncEventArgs e)
        {
            if (e.SocketError != SocketError.Success)
            {
                Debug.LogError("Server.ProcessSend failed: " + e.SocketError);
                CloseClientSocket(e);
            }

            // free buffer chunk
            //Logger.Log("Server.Process send: freeing!");
            bigBuffer.Free(e);
        }

        void OnReceiveClientData(AsyncUserToken token, byte[] data)
        {
            messageQueue.Enqueue(new Message(token.connectionId, EventType.Data, data));
        }

        void CloseClientSocket(SocketAsyncEventArgs e)
        {
            AsyncUserToken token = (AsyncUserToken)e.UserToken;

            clients.TryRemove(token.connectionId, out AsyncUserToken temp);

            // close the socket associated with the client
            try
            {
                token?.Socket.Shutdown(SocketShutdown.Both);
            }
            catch (Exception) { }
            token?.Socket.Close();

            // call disconnected event
            OnClientDisconnected(token);

            // free buffer chunk
            bigBuffer.Free(e);
        }

        public bool Send(int connectionId, byte[] message)
        {
            // find it
            if (clients.TryGetValue(connectionId, out AsyncUserToken token))
            {
                if (token?.Socket == null || !token.Socket.Connected)
                    return false;

                try
                {
                    SocketAsyncEventArgs sendArg = new SocketAsyncEventArgs();
                    sendArg.Completed += IO_Completed; // callback needed to free buffer
                    sendArg.UserToken = token;

                    // assign buffer from BigBuffer for max performance and
                    // initialize with our message
                    byte[] header = Utils.IntToBytesBigEndian(message.Length);
                    if (bigBuffer.Assign(sendArg, header, message))
                    {
                        token.Socket.SendAsync(sendArg);
                        return true;
                    }
                    Debug.Log("Server.Send failed: not enough free chunks! Closing connection because it would be out of sync when sending again.");
                    CloseClientSocket(sendArg);
                }
                catch (Exception e)
                {
                    Debug.LogError("Server.Send failed: " + e);
                }
            }

            return false;
        }
    }
}