﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

//TODO replace copyright notices with MIT licenses

namespace Hazel
{
    /// <summary>
    ///     Listens for new TCP connections and creates TCPConnections for them.
    /// </summary>
    /// <inheritdoc />
    public sealed class TcpConnectionListener : NetworkConnectionListener
    {
        /// <summary>
        ///     The socket listening for connections.
        /// </summary>
        Socket listener;

        /// <summary>
        ///     Creates a new TcpConnectionListener for the given <see cref="IPAddress"/>, port and <see cref="IPMode"/>.
        /// </summary>
        /// <param name="IPAddress">The IPAddress to listen on.</param>
        /// <param name="port">The port to listen on.</param>
        /// <param name="mode">The <see cref="IPMode"/> to listen with.</param>
        public TcpConnectionListener(IPAddress IPAddress, int port, IPMode mode = IPMode.IPv4AndIPv6)
        {
            this.IPAddress = IPAddress;
            this.Port = port;

            if (mode == IPMode.IPv4)
                this.listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            else
            {
                if (!Socket.OSSupportsIPv6)
                    throw new HazelException("IPV6 not supported!");

                this.listener = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
            }
            
            if (mode == IPMode.IPv4AndIPv6)
                this.listener.DualMode = true;
        }

        /// <inheritdoc />
        public override void Start()
        {
            try
            {
                lock (listener)
                {
                    listener.Bind(new IPEndPoint(IPAddress, Port));
                    listener.Listen(1000);

                    listener.BeginAccept(AcceptConnection, null);
                }
            }
            catch (SocketException e)
            {
                throw new HazelException("Could not start listening as a SocketException occured", e);
            }
        }

        /// <summary>
        ///     Called when a new connection has been accepted by the listener.
        /// </summary>
        /// <param name="result">The asyncronous operation's result.</param>
        void AcceptConnection(IAsyncResult result)
        {
            lock (listener)
            {
                //Accept Tcp socket
                Socket tcpSocket;
                try
                {
                    tcpSocket = listener.EndAccept(result);
                }
                catch (ObjectDisposedException)
                {
                    //If the socket's been disposed then we can just end there.
                    return;
                }

                //Start listening for the next connection
                listener.BeginAccept(new AsyncCallback(AcceptConnection), null);

                //Sort the event out
                TcpConnection tcpConnection = new TcpConnection(tcpSocket);

                //Invoke
                InvokeNewConnection(tcpConnection);

                tcpConnection.StartListening();
            }
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (listener)
                    listener.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
