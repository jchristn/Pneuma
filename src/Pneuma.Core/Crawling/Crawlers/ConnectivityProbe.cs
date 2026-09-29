namespace Pneuma.Core.Crawling.Crawlers
{
    using System;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>The DNS and TCP steps of a connectivity test, shared by every crawler.</summary>
    public static class ConnectivityProbe
    {
        #region Public-Methods

        /// <summary>Resolve a host and record the step.</summary>
        /// <param name="result">The result to add the step to.</param>
        /// <param name="host">Host name or address.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the host resolved.</returns>
        public static async Task<bool> ResolveAsync(ConnectivityResult result, string host, CancellationToken token)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            try
            {
                IPAddress[] addresses = await Dns.GetHostAddressesAsync(host, token).ConfigureAwait(false);
                if (addresses.Length == 0) return result.Add("dns", false, host + " did not resolve to any address. Check the host name.");
                return result.Add("dns", true, host + " resolves to " + String.Join(", ", addresses.Take(3).Select(a => a.ToString())) + ".");
            }
            catch (SocketException e)
            {
                return result.Add("dns", false, host + " could not be resolved (" + e.SocketErrorCode + "). Check the host name and the server's DNS.");
            }
        }

        /// <summary>Open (and close) a TCP connection and record the step.</summary>
        /// <param name="result">The result to add the step to.</param>
        /// <param name="host">Host name or address.</param>
        /// <param name="port">Port.</param>
        /// <param name="timeoutMs">Connect timeout in milliseconds.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the port accepted the connection.</returns>
        public static async Task<bool> ConnectAsync(ConnectivityResult result, string host, int port, int timeoutMs, CancellationToken token)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (TcpClient client = new TcpClient())
            {
                timeout.CancelAfter(timeoutMs);
                try
                {
                    await client.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
                    return result.Add("tcp", true, "Connected to " + host + ":" + port + ".");
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    return result.Add("tcp", false, "Connecting to " + host + ":" + port + " timed out. Check that the service is running and a firewall allows the port.");
                }
                catch (SocketException e)
                {
                    return result.Add("tcp", false, "Connecting to " + host + ":" + port + " failed (" + e.SocketErrorCode + "). Check that the service is running and a firewall allows the port.");
                }
            }
        }

        #endregion
    }
}
