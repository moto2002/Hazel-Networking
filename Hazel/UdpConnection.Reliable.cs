﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Hazel
{
    partial class UdpConnection
    {
        /// <summary>
        ///     The starting timeout, in miliseconds, at which data will be resent.
        /// </summary>
        /// <remarks>
        ///     For reliable delivery data is resent at specified intervals unless an acknowledgement is received from the 
        ///     receiving device. The ResendTimeout specifies the interval between the packets being resent, each time a packet
        ///     is resent the interval is doubled for that packet until the number of resends exceeds the 
        ///     <see cref="ResendsBeforeDisconnect"/> value.
        /// </remarks>
        public int ResendTimeout { get { return resendTimeout; } set { resendTimeout = value; } }
        private volatile int resendTimeout = 200;        //TODO this based of average ping?

        /// <summary>
        ///     Holds the last ID allocated.
        /// </summary>
        volatile ushort lastIDAllocated;

        /// <summary>
        ///     The packets of data that have been transmitted reliably and not acknowledged.
        /// </summary>
        Dictionary<ushort, Packet> reliableDataPacketsSent = new Dictionary<ushort, Packet>();

        /// <summary>
        ///     The last packets that were received.
        /// </summary>
        HashSet<ushort> reliableDataPacketsMissing = new HashSet<ushort>();

        /// <summary>
        ///     The packet id that was received last.
        /// </summary>
        volatile ushort reliableReceiveLast = 0;

        /// <summary>
        ///     Has the connection received anything yet
        /// </summary>
        volatile bool hasReceivedSomething = false;

        /// <summary>
        ///     The maximum times a message should be resent before marking the endpoint as disconnected.
        /// </summary>
        /// <remarks>
        ///     Reliable packets will be resent at an interval defined in <see cref="ResendInterval"/> for the number of times
        ///     specified here. Once a packet has been retransmitted this number of times and has not been acknowledged the
        ///     connection will be marked as disconnected and the <see cref="Connection.Disconnected">Disconnected</see> event
        ///     will be invoked.
        /// </remarks>
        public int ResendsBeforeDisconnect { get { return resendsBeforeDisconnect; } set { resendsBeforeDisconnect = value; } }
        private volatile int resendsBeforeDisconnect = 3;

        /// <summary>
        ///     Class to hold packet data
        /// </summary>
        class Packet : IRecyclable, IDisposable
        {
            /// <summary>
            ///     Object pool for this event.
            /// </summary>
            static readonly ObjectPool<Packet> objectPool = new ObjectPool<Packet>(() => new Packet());

            /// <summary>
            ///     Returns an instance of this object from the pool.
            /// </summary>
            /// <returns></returns>
            internal static Packet GetObject()
            {
                return objectPool.GetObject();
            }

            public byte[] Data;
            public Timer Timer;
            public volatile int LastTimeout;
            public Action AckCallback;
            public volatile bool Acknowledged;
            public volatile int Retransmissions;

            Packet()
            {

            }
            
            internal void Set(byte[] data, Action<Packet> resendAction, int timeout, Action ackCallback)
            {
                Data = data;
                
                Timer = new Timer(
                    (object obj) => resendAction(this),
                    null, 
                    timeout,
                    Timeout.Infinite
                );

                LastTimeout = timeout;
                AckCallback = ackCallback;
                Acknowledged = false;
                Retransmissions = 0;
            }

            /// <summary>
            ///     Returns this object back to the object pool from whence it came.
            /// </summary>
            public void Recycle()
            {
                lock (Timer)
                    Timer.Dispose();

                objectPool.PutObject(this);
            }

            /// <summary>
            ///     Disposes of this object.
            /// </summary>
            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            protected void Dispose(bool disposing)
            {
                if (disposing)
                {
                    lock (Timer)
                        Timer.Dispose();
                }
            }
        }

        /// <summary>
        ///     Writes the bytes neccessary for a reliable send and stores the send.
        /// </summary>
        /// <param name="bytes">The byte array to write to.</param>
        void WriteReliableSendHeader(byte[] bytes, Action ackCallback)
        {
            lock (reliableDataPacketsSent)
            {
                //Find an ID not used yet.
                ushort id;

                do
                    id = ++lastIDAllocated;
                while (reliableDataPacketsSent.ContainsKey(id));

                //Write ID
                bytes[1] = (byte)((id >> 8) & 0xFF);
                bytes[2] = (byte)id;

                //Create packet object
                Packet packet = Packet.GetObject();
                packet.Set(
                    bytes,
                    (Packet p) =>
                    {
                        //Double packet timeout
                        lock (p.Timer)
                        {
                            if (!p.Acknowledged)
                            {
                                p.Timer.Change(p.LastTimeout *= 2, Timeout.Infinite);
                                if (++p.Retransmissions > ResendsBeforeDisconnect)
                                {
                                    HandleDisconnect();
                                    p.Recycle();
                                    return;
                                }
                            }
                        }

                        WriteBytesToConnection(p.Data);

                        Trace.WriteLine("Resend.");
                    },
                    resendTimeout,
                    ackCallback
                );

                //Remember packet
                reliableDataPacketsSent.Add(id, packet);
            }
        }

        /// <summary>
        ///     Handles receives from reliable packets.
        /// </summary>
        /// <param name="bytes">The buffer containing the data.</param>
        /// <returns>Whether the bytes were valid or not.</returns>
        bool HandleReliableReceive(byte[] bytes)
        {
            //Get the ID form the packet
            ushort id = (ushort)((bytes[1] << 8) + bytes[2]);

            //Send an acknowledgement
            SendAck(bytes[1], bytes[2]);

            //Handle reliableness!
            lock (reliableDataPacketsMissing)
            {
                //TODO Looping of IDs
                //      Currently when ID loops all packets will be discarded as ID will be less than reliableReceiveLast
                //      And wont be in reliableDataPacketsMissing.

                //If the ID <= reliableReceiveLast it might be something we're missing
                //HasReceivedSomething handles the edge case of reliableReceiveLast = 0 & ID = 0
                if (id <= reliableReceiveLast && hasReceivedSomething)
                {
                    //See if we're missing it, else this packet is a duplicate
                    if (reliableDataPacketsMissing.Contains(id))
                        reliableDataPacketsMissing.Remove(id);
                    else
                        return false;
                }
                
                //If ID > reliableReceiveLast then it's something new
                else
                {
                    //Mark items between the most recent receive and the id received as missing
                    for (ushort i = (ushort)(reliableReceiveLast + 1); i < id; i++)
                        reliableDataPacketsMissing.Add(i);

                    //Update the most recently received
                    reliableReceiveLast = id;
                    hasReceivedSomething = true;
                }
            }

            return true;
        }

        /// <summary>
        ///     Handles acknowledgement packets to us.
        /// </summary>
        /// <param name="bytes">The buffer containing the data.</param>
        void HandleAcknowledgement(byte[] bytes)
        {
            //Get ID
            ushort id = (ushort)((bytes[1] << 8) + bytes[2]);

            lock (reliableDataPacketsSent)
            {
                //Dispose of timer and remove from dictionary
                if (reliableDataPacketsSent.ContainsKey(id))
                {
                    Packet packet = reliableDataPacketsSent[id];
                    
                    packet.Acknowledged = true;

                    if (packet.AckCallback != null)
                        packet.AckCallback.Invoke();

                    packet.Recycle();

                    reliableDataPacketsSent.Remove(id);
                }
            }
        }

        /// <summary>
        ///     Sends an acknowledgement for a packet given its identification bytes.
        /// </summary>
        /// <param name="byte1">The first identification byte.</param>
        /// <param name="byte2">The second identification byte.</param>
        internal void SendAck(byte byte1, byte byte2)
        {
            //Always reply with acknowledgement in order to stop the sender repeatedly sending it
            WriteBytesToConnection(     //TODO group acks together
                new byte[]
                {
                    (byte)SendOptionInternal.Acknowledgement,
                    byte1,
                    byte2
                }
            );
        }
    }
}
