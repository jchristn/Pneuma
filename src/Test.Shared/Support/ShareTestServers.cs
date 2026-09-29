namespace Test.Shared.Support
{
    using System;
    using System.IO;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using OpenCIFS.Server;
    using OpenNFS.Server;
    using OpenNFS.Server.FileHandles;

    /// <summary>
    /// In-process OpenCIFS and OpenNFS servers over a temporary directory, bound to 127.0.0.1 on free ports, for the
    /// CIFS and NFS crawler tests. Disposing stops the servers and deletes the directory.
    /// </summary>
    public sealed class ShareTestServers : IAsyncDisposable
    {
        #region Public-Members

        /// <summary>The directory both servers share (the CIFS share root and the NFS export root).</summary>
        public string Root { get; }

        /// <summary>CIFS port.</summary>
        public int CifsPort { get; private set; } = 0;

        /// <summary>CIFS share name.</summary>
        public string CifsShare { get; } = "docs";

        /// <summary>CIFS user name.</summary>
        public string CifsUser { get; } = "crawler";

        /// <summary>CIFS password.</summary>
        public string CifsPassword { get; } = "Crawl-Test-1!";

        /// <summary>NFS port.</summary>
        public int NfsPort { get; private set; } = 0;

        /// <summary>MOUNT port.</summary>
        public int MountPort { get; private set; } = 0;

        /// <summary>NFS export path.</summary>
        public string NfsExport { get; } = "/data";

        #endregion

        #region Private-Members

        private OpenCifsServerApplication? _Cifs = null;
        private OpenNfsServerApplication? _Nfs = null;
        private readonly string _State;

        #endregion

        #region Constructors-and-Factories

        private ShareTestServers()
        {
            Root = Path.Combine(Path.GetTempPath(), "pneuma-shares-" + Guid.NewGuid().ToString("N"));
            _State = Root + "-state";
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(_State);
        }

        /// <summary>Start the servers.</summary>
        /// <param name="cifs">Start the CIFS server.</param>
        /// <param name="nfs">Start the NFS server.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The servers.</returns>
        public static async Task<ShareTestServers> StartAsync(bool cifs, bool nfs, CancellationToken ct)
        {
            ShareTestServers servers = new ShareTestServers();
            try
            {
                if (cifs)
                {
                    servers.CifsPort = FreePort();
                    servers._Cifs = new OpenCifsServerBuilder()
                        .WithServerName("pneuma-test")
                        .WithBindAddress("127.0.0.1")
                        .WithBindPort(servers.CifsPort)
                        .AddAccount(new OpenCifsServerAccount { UserName = servers.CifsUser, UserDomain = "WORKGROUP", Password = servers.CifsPassword })
                        .AddShare(servers.CifsShare, share => share.UseLocalFileSystem(servers.Root))
                        .Build()
                        .BuildApplication(e => { });
                    await servers._Cifs.StartAsync(ct).ConfigureAwait(false);
                }
                if (nfs)
                {
                    servers.NfsPort = FreePort();
                    servers.MountPort = FreePort();
                    servers._Nfs = new OpenNfsServerBuilder()
                        .WithServerName("pneuma-test")
                        .WithListenerAddress("127.0.0.1")
                        .UseLocalFileSystem()
                        .UseFileHandleProvider(new PersistentMappingHandleProvider(Path.Combine(servers._State, "filehandles.json")))
                        .AddExport(servers.NfsExport, servers.Root)
                        .BuildApplication(new OpenNfsServerApplicationOptions
                        {
                            ListenerAddress = "127.0.0.1",
                            NfsPort = servers.NfsPort,
                            MountPort = servers.MountPort,
                            Nfs40Port = FreePort(),
                            EnableNfs40 = false,
                            EnableNfs41 = false,
                            EnableNfs42 = false
                        });
                    await servers._Nfs.StartAsync(ct).ConfigureAwait(false);
                }
                return servers;
            }
            catch (Exception)
            {
                await servers.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>Write a file under the root (creating folders), with its modification time set to now.</summary>
        /// <param name="relative">Path relative to the root, with forward slashes.</param>
        /// <param name="content">Text content.</param>
        public void Write(string relative, string content)
        {
            string path = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        }

        /// <summary>Delete a file under the root.</summary>
        /// <param name="relative">Path relative to the root.</param>
        public void Delete(string relative)
        {
            File.Delete(Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar)));
        }

        /// <summary>Stop the servers and delete the directories.</summary>
        /// <returns>A task.</returns>
        public async ValueTask DisposeAsync()
        {
            if (_Cifs != null)
            {
                try { await _Cifs.StopAsync(CancellationToken.None).ConfigureAwait(false); } catch (Exception) { }
                try { await _Cifs.DisposeAsync().ConfigureAwait(false); } catch (Exception) { }
            }
            if (_Nfs != null)
            {
                try { await _Nfs.StopAsync(CancellationToken.None).ConfigureAwait(false); } catch (Exception) { }
                try { await _Nfs.DisposeAsync().ConfigureAwait(false); } catch (Exception) { }
            }
            foreach (string dir in new[] { Root, _State })
            {
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        #endregion

        #region Private-Methods

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
