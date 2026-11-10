//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Emgu.LiteRT.LM.Server
{
    /// <summary>
    /// A client's side of a server on the local network that may be asleep or gone (a paired Mac): HTTP handlers that give
    /// up connecting after a few seconds instead of the system's minutes, and don't reuse a connection idle long enough for
    /// the server to have slept; and whether a request failed because the server couldn't be reached.
    /// </summary>
    public static class ServerReachability
    {
        /// <summary>How long a connection may take before the server counts as unreachable: 6 s (a Mac woken by the network
        /// through a Bonjour Sleep Proxy answers within it).</summary>
        public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(6);

        /// <summary>How long an idle connection is kept for the next request: 30 s.</summary>
        public static readonly TimeSpan IdleConnectionTimeout = TimeSpan.FromSeconds(30);

        /// <summary>
        /// A handler for requests to the server: connecting gives up after <paramref name="connectTimeout"/>, idle
        /// connections are closed after <see cref="IdleConnectionTimeout"/>; a reply itself may take as long as the client
        /// allows. (On .NET Standard 2.0, the platform's handler.)
        /// </summary>
        /// <param name="connectTimeout">The connection's limit, or null for <see cref="ConnectTimeout"/></param>
        public static HttpMessageHandler CreateHandler(TimeSpan? connectTimeout = null)
        {
#if NET
            TimeSpan limit = connectTimeout ?? ConnectTimeout;
            return new SocketsHttpHandler
            {
                PooledConnectionIdleTimeout = IdleConnectionTimeout,
                // The handler's own ConnectTimeout throws what HttpClient.Timeout throws (a cancellation with a
                // TimeoutException inside): a connection of our own makes it a SocketException, TimedOut.
                ConnectCallback = (context, cancel) => ConnectAsync(context.DnsEndPoint, limit, cancel)
            };
#else
            return new HttpClientHandler();
#endif
        }

#if NET
        private static async ValueTask<System.IO.Stream> ConnectAsync(System.Net.EndPoint endPoint, TimeSpan limit, CancellationToken cancel)
        {
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                timeout.CancelAfter(limit);
                try
                {
                    await socket.ConnectAsync(endPoint, timeout.Token).ConfigureAwait(false);
                    return new NetworkStream(socket, true);
                }
                catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
                {
                    socket.Dispose();
                    throw new SocketException((int)SocketError.TimedOut);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        }
#endif

        /// <summary>
        /// Whether a request failed before reaching the server: the connection was refused, timed out, or found no route or
        /// host (the server asleep, stopped, or at another address) - not an answer of the server's, nor a reply cut off.
        /// </summary>
        /// <param name="error">What the request threw (looked into through its inner exceptions)</param>
        public static bool IsUnreachable(Exception error)
        {
            for (Exception e = error; e != null; e = e.InnerException)
            {
                if (e is SocketException socket)
                    return socket.SocketErrorCode == SocketError.ConnectionRefused || socket.SocketErrorCode == SocketError.TimedOut
                        || socket.SocketErrorCode == SocketError.HostUnreachable || socket.SocketErrorCode == SocketError.NetworkUnreachable
                        || socket.SocketErrorCode == SocketError.HostNotFound || socket.SocketErrorCode == SocketError.HostDown
                        || socket.SocketErrorCode == SocketError.NetworkDown;
            }
            return false;
        }
    }
}
