namespace RenegotiateIssue
{
    using System;
    using System.Net;
    using System.Net.Security;
    using System.Net.Sockets;
    using System.Security.Authentication;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;
    using System.Threading;
    using System.Threading.Tasks;

    public enum TestScenario
    {
        NoCertRequest,

        CertRequest,

        RenegotiateCertRequest,
    }

    internal class TestTlsServer : IDisposable
    {
        private static readonly X509Certificate2 ServerCertificate = CreateCertificate("CN=Test Server");

        private readonly TestScenario scenario;

        private Task serverTask;

        private CancellationTokenSource shutdownCts;

        public int ReadSize { get; set; } = 0;

        public TestTlsServer(TestScenario scenario, int port = 5005)
        {
            this.scenario = scenario;
            this.shutdownCts = new CancellationTokenSource();
            this.serverTask = this.RunServer(port);
        }

        private async Task RunServer(int port)
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            var endpoint = new IPEndPoint(IPAddress.Any, port);
            socket.Bind(endpoint);

            socket.Listen();

            await this.AcceptWorker(socket);
        }

        private async Task AcceptWorker(Socket socket)
        {
            while (true)
            {
                try
                {
                    using Socket clientSocket = await socket.AcceptAsync(this.shutdownCts.Token);

                    using var networkStream = new NetworkStream(clientSocket, ownsSocket: false);

                    await this.DoTlsHandshake(networkStream);
                }
                catch (Exception e)
                {
                    if (!this.shutdownCts.Token.IsCancellationRequested)
                    {
                        Console.WriteLine("Server error: " + e);
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }

        private async Task DoTlsHandshake(NetworkStream stream)
        {
            using var sslStream = new SslStream(stream, leaveInnerStreamOpen: true);

            var options = new SslServerAuthenticationOptions()
            {
                ClientCertificateRequired = this.scenario == TestScenario.CertRequest,
                ServerCertificate = ServerCertificate,
                EnabledSslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12,
                RemoteCertificateValidationCallback = (a, b, c, d) => true,
            };

            await sslStream.AuthenticateAsServerAsync(options);

            await ReadBytes(sslStream, this.ReadSize);

            if (this.scenario == TestScenario.RenegotiateCertRequest && sslStream.RemoteCertificate == null)
            {
                await sslStream.NegotiateClientCertificateAsync();
            }


            await sslStream.WriteAsync(new byte[1]);
        }

        public void Dispose()
        {
            this.shutdownCts.Cancel();

            try
            {
                this.serverTask.Wait(TimeSpan.FromSeconds(5));
            }
            catch
            {
            }
        }

        private static async Task ReadBytes(Stream stream, int bytes)
        {
            var buf = new byte[100];

            int leftToRead = bytes;
            while (leftToRead > 0)
            {
                int result = await stream.ReadAsync(buf, 0, leftToRead);

                if (result == 0)
                {
                    break;
                }

                leftToRead -= result;
            }
        }

        public static X509Certificate2 CreateCertificate(string subjectName)
        {
            var rsa = RSA.Create();
            var req = new CertificateRequest(subjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            var oids = new OidCollection
            {
                new Oid("1.3.6.1.5.5.7.3.1")
            };

            var eku = new X509EnhancedKeyUsageExtension(oids, true);

            req.CertificateExtensions.Add(eku);

            var cert = req.CreateSelfSigned(
                DateTimeOffset.Now - TimeSpan.FromHours(1),
                DateTimeOffset.Now.AddYears(1));

            byte[] pfx = cert.Export(X509ContentType.Pfx);

            return new X509Certificate2(pfx, string.Empty, X509KeyStorageFlags.MachineKeySet);
        }
    }
}
