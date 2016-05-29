﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Net;

namespace Hazel
{
    /// <summary>
    ///     Represents an endpoint to a remote resource on a network.
    /// </summary>
    /// <remarks>
    ///     This wraps a <see cref="System.Net.EndPoint"/> for connecting across a network using protocols like TCP or UDP.
    /// </remarks>
    public sealed class NetworkEndPoint : ConnectionEndPoint
    {
        /// <summary>
        ///     The <see cref="System.Net.EndPoint">EndPoint</see> this points to.
        /// </summary>
        public EndPoint EndPoint { get; set; }

        /// <summary>
        ///     The <see cref="IPMode"/> this will instruct connections to use.
        /// </summary>
        public IPMode IPMode { get; set; }

        /// <summary>
        ///     Creates a NetworkEndPoint from a given <see cref="System.Net.EndPoint">EndPoint</see>.
        /// </summary>
        /// <param name="endPoint">The end point to wrap./param>
        public NetworkEndPoint(EndPoint endPoint, IPMode mode = IPMode.IPv4AndIPv6)
        {
            this.EndPoint = endPoint;
            this.IPMode = mode;
        }

        /// <summary>
        ///     Create a NetworkEndPoint to the specified <see cref="System.Net.IPAddress">IPAddress</see> and port.
        /// </summary>
        /// <param name="address">The IP address of the server.</param>
        /// <param name="port">The port the server is listening on.</param>
        /// <remarks>
        ///     When using this constructor <see cref="EndPoint"/> will contain an <see cref="IPEndPoint"/>.
        /// </remarks>
        public NetworkEndPoint(IPAddress address, int port, IPMode mode = IPMode.IPv4AndIPv6)
            : this(new IPEndPoint(address, port))
        {

        }

        /// <summary>
        ///     Creates a NetworkEndPoint to the specified IP address and port.
        /// </summary>
        /// <param name="IP">A valid IP address of the server.</param>
        /// <param name="port">The port the server is listening on.</param>
        /// <remarks>
        ///     When using this constructor <see cref="EndPoint"/> will contain an <see cref="IPEndPoint"/>.
        /// </remarks>
        public NetworkEndPoint(string IP, int port, IPMode mode = IPMode.IPv4AndIPv6)
            : this(IPAddress.Parse(IP), port)
        {

        }
    }
}
