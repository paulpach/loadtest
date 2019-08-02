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
    public class Client : Common, IDisposable
    {
        // The socket used to send/receive messages.
        Socket clientSocket;

        readonly MemoryStream buffer = new MemoryStream();

        public bool Connected  { get; private set; }

        public bool Connecting => clientSocket != null && !Connected;

        public EndPoint RemoteEndPoint => clientSocket?.RemoteEndPoint;

        // incoming message queue
        readonly ConcurrentQueue<Message> incomingQueue = new ConcurrentQueue<Message>();

        // removes and returns the oldest message from the message queue.
        // (might want to call this until it doesn't return anything anymore)
        // -> Connected, Data, Disconnected events are all added here
        // -> bool return makes while (GetMessage(out Message)) easier!
        // -> no 'is client connected' check because we still want to read the
        //    Disconnected message after a disconnect
        public bool GetNextMessage(out Message message)
        {
            return incomingQueue.TryDequeue(out message);
        }

        public bool Connect(string ip, int port)
        {
            // Instantiate the endpoint and socket.

            clientSocket = new Socket(SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true
            };

            IPAddress[] addresses = Dns.GetHostAddresses(ip);


            SocketAsyncEventArgs connectArgs = new SocketAsyncEventArgs
            {
                UserToken = clientSocket,
                RemoteEndPoint = new IPEndPoint(addresses[addresses.Length - 1], port)
            };
            connectArgs.Completed += ProcessConnect;

            if (!clientSocket.ConnectAsync(connectArgs))
            {
                ProcessConnect(null, connectArgs);
            }
            return true;
        }

        // Disconnect from the host.
        public void Disconnect()
        {
            try
            {
                clientSocket?.Disconnect(false);
            }
            catch (ObjectDisposedException)
            {
                // it is ok if it is dispossed
            }
        }

        // Callback for connect operation
        void ProcessConnect(object sender, SocketAsyncEventArgs e)
        {
            // Set the flag for socket connected.
            Connected = (e.SocketError == SocketError.Success);
            if (Connected)
            {
                Debug.Log("Connected");
                incomingQueue.Enqueue(new Message(0, EventType.Connected, null));

                // create SocketAsyncEventArgs for receive
                SocketAsyncEventArgs args = new SocketAsyncEventArgs();
                args.Completed += IO_Completed;
                args.UserToken = e.UserToken;

                // assign chunk of big buffer for max performance (see BigBuffer.cs comments)
                if (bigBuffer.Assign(args))
                {
                    if (!e.ConnectSocket.ReceiveAsync(args))
                        ProcessReceive(args);
                }
                else Debug.LogError("Client.InitArgs: failed to assign buffer");
            }
            else
            {
                Debug.LogError("Failed to connect " + e.SocketError);
            }
        }

        // This method is invoked when an asynchronous receive operation completes.
        // If the remote host closed the connection, then the socket is closed.
        // If data was received then the data is echoed back to the client.
        //
        protected override void ProcessReceive(SocketAsyncEventArgs e)
        {
            try
            {
                // check if the remote host closed the connection
                Socket token = (Socket)e.UserToken;
                if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
                {
                    // write it all into our memory stream first
                    buffer.Write(e.Buffer, e.Offset, e.BytesTransferred);

                    // keep trying headers (we might need to process >1 message)
                    while (buffer.Position >= 4)
                    {
                        // we can read a header. so read it.
                        long bufferSize = buffer.Position;
                        buffer.Position = 0;
                        byte[] header = new byte[4]; // TODO cache
                        buffer.Read(header, 0, header.Length);
                        int contentSize = Utils.BytesToIntBigEndian(header);

                        // avoid -1 attacks from hackers
                        if (contentSize > 0)
                        {
                            // enough content to finish the message?
                            if (bufferSize - buffer.Position >= contentSize)
                            {
                                // read content
                                byte[] content = new byte[contentSize];
                                buffer.Read(content, 0, content.Length);

                                // process message
                                DoReceiveEvent(content);

                                // read what's left in the buffer. this is valid
                                // data that we received at some point. can't lose
                                // it.
                                byte[] remainder = new byte[bufferSize - buffer.Position];
                                buffer.Read(remainder, 0, remainder.Length);

                                // write it to the beginning of the buffer. this
                                // sets position to the new true end automatically.
                                buffer.Position = 0;
                                buffer.Write(remainder, 0, remainder.Length);
                            }
                            // otherwise we just need to receive more.
                            else break;
                        }
                        else
                        {
                            ProcessError(e);
                            Debug.LogWarning("Client.ProcessReceive: received negative contentSize: " + contentSize + ". Maybe an attacker tries to exploit the server?");
                        }
                    }

                    if (!token.ReceiveAsync(e))
                        ProcessReceive(e);
                }
                else
                {
                    // connection was closed
                    ProcessError(e);
                }
            }
            catch (Exception exception)
            {
                ProcessError(e);
                Debug.Log("Client.ProcessReceive failed: " + exception);
            }
        }

        // This method is invoked when an asynchronous send operation completes.
        protected override void ProcessSend(SocketAsyncEventArgs e)
        {
            if (e.SocketError != SocketError.Success)
            {
                ProcessError(e);
            }

            // free buffer chunk
            bigBuffer.Free(e);
            //Logger.Log("Client.Process send: freeing!");
        }

        // Close socket in case of failure
        void ProcessError(SocketAsyncEventArgs e)
        {
            Socket s = (Socket)e.UserToken;
            if (s.Connected)
            {
                // close the socket associated with the client
                try
                {
                    s.Shutdown(SocketShutdown.Both);
                }
#pragma warning disable RECS0022 // A catch clause that catches System.Exception and has an empty body
                catch (Exception)
#pragma warning restore RECS0022 // A catch clause that catches System.Exception and has an empty body
                {
                    // throws if client process has already closed
                }
                finally
                {
                    if (s.Connected)
                    {
                        s.Close();
                    }
                    Connected = false;
                }
            }

            e.Completed -= IO_Completed;

            // free buffer chunk
            bigBuffer.Free(e);

            // disconnected event
            incomingQueue.Enqueue(new Message(0, EventType.Disconnected, null));
        }

        // Exchange a message with the host.
        public bool Send(byte[] message)
        {
            if (Connected)
            {
                SocketAsyncEventArgs sendArgs = new SocketAsyncEventArgs();
                sendArgs.Completed += IO_Completed; // callback needed to free buffer
                sendArgs.UserToken = clientSocket;

                // assign buffer from BigBuffer for max performance and
                // initialize with our message
                byte[] header = Utils.IntToBytesBigEndian(message.Length);
                if (bigBuffer.Assign(sendArgs, header, message))
                {
                    clientSocket.SendAsync(sendArgs);
                    return true;
                }
                Debug.Log("Server.Send failed: not enough free chunks! Closing connection because it would be out of sync when sending again.");
                ProcessError(sendArgs);
                return false;
            }
            else
            {
                //throw new SocketException((int)SocketError.NotConnected);
                return false;
            }
        }

        void DoReceiveEvent(byte[] buff)
        {
            incomingQueue.Enqueue(new Message(0, EventType.Data, buff));
        }

        // Disposes the instance of SocketClient.
        public void Dispose()
        {
            clientSocket?.Close();
        }
    }
}