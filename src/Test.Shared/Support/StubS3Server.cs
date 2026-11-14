namespace Test.Shared.Support
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A minimal path-style S3 service on 127.0.0.1 for blob-store tests: bucket create and probe, and object put, get,
    /// and delete. Signatures are not checked. Bodies the AWS SDK sends with <c>aws-chunked</c> encoding are decoded.
    /// </summary>
    public sealed class StubS3Server : IDisposable
    {
        #region Public-Members

        /// <summary>The service URL, for example <c>http://127.0.0.1:50123</c>.</summary>
        public string Endpoint { get; }

        /// <summary>Buckets that exist.</summary>
        public ConcurrentDictionary<string, bool> Buckets { get; } = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);

        /// <summary>Stored objects keyed by <c>bucket/key</c>.</summary>
        public ConcurrentDictionary<string, byte[]> Objects { get; } = new ConcurrentDictionary<string, byte[]>(StringComparer.Ordinal);

        #endregion

        #region Private-Members

        private readonly HttpListener _Listener = new HttpListener();
        private readonly CancellationTokenSource _Stop = new CancellationTokenSource();
        private readonly Task _Loop;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Start the stub on a free loopback port.</summary>
        public StubS3Server()
        {
            int port = FreePort();
            Endpoint = "http://127.0.0.1:" + port;
            _Listener.Prefixes.Add(Endpoint + "/");
            _Listener.Start();
            _Loop = Task.Run(LoopAsync);
        }

        #endregion

        #region Public-Methods

        /// <summary>Stop the stub.</summary>
        public void Dispose()
        {
            _Stop.Cancel();
            try { _Listener.Stop(); } catch (Exception) { }
            try { _Listener.Close(); } catch (Exception) { }
            _Stop.Dispose();
        }

        #endregion

        #region Private-Methods

        private async Task LoopAsync()
        {
            while (!_Stop.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await _Listener.GetContextAsync().ConfigureAwait(false); }
                catch (Exception) { return; }
                _ = Task.Run(() => Handle(ctx));
            }
        }

        private void Handle(HttpListenerContext ctx)
        {
            try
            {
                string path = Uri.UnescapeDataString(ctx.Request.Url!.AbsolutePath.TrimStart('/'));
                int slash = path.IndexOf('/');
                string bucket = slash < 0 ? path : path.Substring(0, slash);
                string key = slash < 0 ? String.Empty : path.Substring(slash + 1);
                string method = ctx.Request.HttpMethod;

                if (key.Length == 0)
                {
                    if (method == "PUT")
                    {
                        Buckets[bucket] = true;
                        Send(ctx, 200, null);
                    }
                    else if (Buckets.ContainsKey(bucket))
                    {
                        Send(ctx, 200, method == "HEAD" ? null : "<?xml version=\"1.0\" encoding=\"UTF-8\"?><AccessControlPolicy><Owner><ID>stub</ID></Owner><AccessControlList></AccessControlList></AccessControlPolicy>");
                    }
                    else
                    {
                        Error(ctx, 404, "NoSuchBucket");
                    }
                    return;
                }

                if (!Buckets.ContainsKey(bucket))
                {
                    Error(ctx, 404, "NoSuchBucket");
                    return;
                }

                string id = bucket + "/" + key;
                switch (method)
                {
                    case "PUT":
                        Objects[id] = ReadBody(ctx.Request);
                        ctx.Response.AddHeader("ETag", "\"stub\"");
                        Send(ctx, 200, null);
                        return;
                    case "GET":
                        if (!Objects.TryGetValue(id, out byte[]? data)) { Error(ctx, 404, "NoSuchKey"); return; }
                        ctx.Response.StatusCode = 200;
                        ctx.Response.ContentType = "application/octet-stream";
                        ctx.Response.ContentLength64 = data.Length;
                        ctx.Response.OutputStream.Write(data, 0, data.Length);
                        ctx.Response.Close();
                        return;
                    case "DELETE":
                        Objects.TryRemove(id, out byte[]? _);
                        Send(ctx, 204, null);
                        return;
                    default:
                        Send(ctx, Objects.ContainsKey(id) ? 200 : 404, null);
                        return;
                }
            }
            catch (Exception)
            {
                try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch (Exception) { }
            }
        }

        // The SDK may send aws-chunked bodies ("hex;chunk-signature=...\r\n<data>\r\n ... 0\r\n<trailers>\r\n\r\n").
        private static byte[] ReadBody(HttpListenerRequest request)
        {
            byte[] raw;
            using (MemoryStream buffer = new MemoryStream())
            {
                request.InputStream.CopyTo(buffer);
                raw = buffer.ToArray();
            }

            string encoding = request.Headers["Content-Encoding"] ?? String.Empty;
            string sha = request.Headers["x-amz-content-sha256"] ?? String.Empty;
            bool awsChunked = encoding.Contains("aws-chunked", StringComparison.OrdinalIgnoreCase) || sha.StartsWith("STREAMING", StringComparison.OrdinalIgnoreCase);
            if (!awsChunked) return raw;

            List<byte> decoded = new List<byte>();
            int pos = 0;
            while (pos < raw.Length)
            {
                int lineEnd = IndexOfCrlf(raw, pos);
                if (lineEnd < 0) break;
                string header = Encoding.ASCII.GetString(raw, pos, lineEnd - pos);
                string hex = header.Split(';')[0].Trim();
                int size = Convert.ToInt32(hex, 16);
                pos = lineEnd + 2;
                if (size == 0) break;
                decoded.AddRange(raw.Skip(pos).Take(size));
                pos += size + 2;
            }

            return decoded.ToArray();
        }

        private static int IndexOfCrlf(byte[] data, int start)
        {
            for (int i = start; i < data.Length - 1; i++)
            {
                if (data[i] == '\r' && data[i + 1] == '\n') return i;
            }

            return -1;
        }

        private static void Error(HttpListenerContext ctx, int status, string code)
        {
            ctx.Response.ContentType = "application/xml";
            Send(ctx, status, "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Error><Code>" + code + "</Code><Message>" + code + "</Message></Error>");
        }

        private static void Send(HttpListenerContext ctx, int status, string? body)
        {
            ctx.Response.StatusCode = status;
            if (body != null)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(body);
                if (String.IsNullOrEmpty(ctx.Response.ContentType)) ctx.Response.ContentType = "application/xml";
                ctx.Response.ContentLength64 = bytes.Length;
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            }

            ctx.Response.Close();
        }

        private static int FreePort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        #endregion
    }
}
