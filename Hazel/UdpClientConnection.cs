﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Hazel
{
    /// <summary>
    ///     Represents a client's connection to a server that uses the UDP protocol.
    /// </summary>
    /// <inheritdoc/>
    public sealed class UdpClientConnection : UdpConnection
    {
        /// <summary>
        ///     The socket we're connected via.
        /// </summary>
        Socket socket;

        /// <summary>
        ///     The lock for the socket.
        /// </summary>
        Object socketLock = new Object();

        /// <summary>
        ///     The buffer to store incomming data in.
        /// </summary>
        byte[] dataBuffer = new byte[ushort.MaxValue];

        /// <summary>
        ///     Creates a new UdpClientConnection.
        /// </summary>
        public UdpClientConnection()
            : base()
        {
            
        }

        /// <inheritdoc />
        protected override void WriteBytesToConnection(byte[] bytes)
        {
            //Pack
            SocketAsyncEventArgs args = new SocketAsyncEventArgs();
            args.SetBuffer(bytes, 0, bytes.Length);
            args.RemoteEndPoint = RemoteEndPoint;

            lock (socketLock)
            {
                if (State != ConnectionState.Connected && State != ConnectionState.Connecting)
                    throw new InvalidOperationException("Could not send data as this Connection is not connected and is not connecting. Did you disconnect?");

                try
                {
                    socket.SendToAsync(args);
                }
                catch (ObjectDisposedException)
                {
                    //User probably called Disconnect in between this method starting and here so report the issue
                    throw new InvalidOperationException("Could not send data as this Connection is not connected. Did you disconnect?");
                }
                catch (SocketException e)
                {
                    HazelException he = new HazelException("Could not send data as a SocketException occured.", e);
                    HandleDisconnect(he);
                    throw he;
                }
            }
        }

        /// <inheritdoc />
        /// <param name="remoteEndPoint">A <see cref="NetworkEndPoint"/> to connect to.</param>
        public override void Connect(ConnectionEndPoint remoteEndPoint)
        {
            NetworkEndPoint nep = remoteEndPoint as NetworkEndPoint;
            if (nep == null)
            {
                throw new ArgumentException("The remote end point of a UDP connection must be a NetworkEndPoint.");
            }

            lock (socketLock)
            {
                this.EndPoint = nep;
                this.RemoteEndPoint = nep.EndPoint;

                if (nep.IPMode == IPMode.IPv4)
                    socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                else
                {
                    socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
                    socket.DualMode = true;
                }

                if (State != ConnectionState.NotConnected)
                    throw new InvalidOperationException("Cannot connect as the Connection is already connected.");

                State = ConnectionState.Connecting;

                //Calculate local end point
                EndPoint localEndPoint;
                if (nep.EndPoint is IPEndPoint)
                    localEndPoint = new IPEndPoint(((IPEndPoint)nep.EndPoint).Address, 0);
                else if (nep.EndPoint is IPEndPoint)
                    localEndPoint = new DnsEndPoint(((DnsEndPoint)nep.EndPoint).Host, 0);
                else
                    throw new ArgumentException("Can only connect using an IPEndPoint or DnsEndpoint");

                //Begin listening
                try
                {
                    socket.Bind(localEndPoint);
                }
                catch (SocketException e)
                {
                    throw new HazelException("A socket exception occured while binding to the port.", e);
                }

                try
                {
                    StartListeningForData();
                }
                catch (ObjectDisposedException)
                {
                    //If the socket's been disposed then we can just end there but make sure we're in NotConnected state.
                    //If we end up here I'm really lost...
                    State = ConnectionState.NotConnected;
                    return;
                }
                catch (SocketException e)
                {
                    throw new HazelException("A Socket exception occured while initiating a receive operation.", e);
                }
            }

            //Write bytes to the server to tell it hi (and to punch a hole in our NAT, if present)
            //When acknowledged set the state to connected
            SendHello(() => { lock (socketLock) State = ConnectionState.Connected; });

            //Wait till hello packet is acknowledged and the state is set to Connected
            WaitOnConnect();
        }

        /// <summary>
        ///     Instructs the listener to begin listening.
        /// </summary>
        void StartListeningForData()
        {
            lock (socketLock)
                socket.BeginReceive(dataBuffer, 0, dataBuffer.Length, SocketFlags.None, ReadCallback, dataBuffer);
        }

        /// <summary>
        ///     Called when data has been received by the socket.
        /// </summary>
        /// <param name="result">The asyncronous operation's result.</param>
        void ReadCallback(IAsyncResult result)
        {
            int bytesReceived;

            //End the receive operation
            try
            {
                lock (socketLock)
                    bytesReceived = socket.EndReceive(result);
            }
            catch (ObjectDisposedException)
            {
                //If the socket's been disposed then we can just end there.
                return;
            }
            catch (SocketException e)
            {
                HandleDisconnect(new HazelException("A socket exception occured while reading data.", e));
                return;
            }

            //Exit if no bytes read, we've failed.
            if (bytesReceived == 0)
            {
                HandleDisconnect();
                return;
            }

            //Decode the data received
            byte[] buffer = HandleReceive(dataBuffer, bytesReceived);
            SendOption sendOption = (SendOption)dataBuffer[0];

            //TODO may get better performance with Handle receive after and block copy call added

            //Begin receiving again
            try
            {
                StartListeningForData();
            }
            catch (SocketException e)
            {
                HandleDisconnect(new HazelException("A Socket exception occured while initiating a receive operation.", e));
            }
            catch (ObjectDisposedException)
            {
                //If the socket's been disposed then we can just end there.
                return;
            }
            
            if (buffer != null)
                InvokeDataReceived(buffer, sendOption);
        }

        /// <inheritdoc />
        protected override void HandleDisconnect(HazelException e = null)
        {
            bool invoke = false;

            lock (socketLock)
            {
                //Only invoke the disconnected event if we're not already disconnecting
                if (State == ConnectionState.Connected)
                {
                    State = ConnectionState.Disconnecting;
                    invoke = true;
                }
            }

            //Invoke event outide lock if need be
            if (invoke)
            {
                InvokeDisconnected(e);

                Dispose();
            }
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            //Dispose of the socket
            if (disposing)
            {
                lock (socketLock)
                {
                    State = ConnectionState.NotConnected;

                    socket.Dispose();
                }
            }

            base.Dispose(disposing);
        }
    }
}
