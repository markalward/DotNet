namespace RenegotiateIssue
{
    using System;
    using System.Net.Security;
    using System.Net.Sockets;
    using System.Net;
    using System.Security.Cryptography.X509Certificates;
    using System.Threading.Tasks;
    using System.Security.Authentication;

    public static class Program
    {
        private static readonly X509Certificate2 ClientCertificate = TestTlsServer.CreateCertificate("CN=Test Client");

        static readonly int ClientWriteSize = 0;

        public static void Main(string[] args)
        {
            Console.WriteLine(System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);

            using (var server = new TestTlsServer(TestScenario.RenegotiateCertRequest))
            {
                server.ReadSize = ClientWriteSize;

                int i = 0;
                try
                {
                    for (; i < 1000; i++)
                    {
                        DoClientHandshake();
                    }
                }
                catch
                {
                    Console.WriteLine($"Failed after {i} iterations");
                    throw;
                }
            }
        }

        private static void DoClientHandshake(string serverName = "localhost", int port = 5005) =>
            DoClientHandshakeAsync(serverName, port).GetAwaiter().GetResult();

        private static async Task DoClientHandshakeAsync(string serverName = "localhost", int port = 5005)
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            var endpoint = new IPEndPoint(IPAddress.Loopback, port);
            await socket.ConnectAsync(endpoint);

            using var networkStream = new NetworkStream(socket, ownsSocket: false);
            using var sslStream = new SslStream(networkStream, leaveInnerStreamOpen: true);

            var options = new SslClientAuthenticationOptions()
            {
                EnabledSslProtocols = SslProtocols.Tls12,
                TargetHost = serverName,
                ClientCertificates = new X509CertificateCollection()
                {
                    ClientCertificate,
                },
                RemoteCertificateValidationCallback = (a, b, c, d) => true,
            };

            await sslStream.AuthenticateAsClientAsync(options);

            if (ClientWriteSize > 0)
            {
                sslStream.Write(new byte[ClientWriteSize]);
            }

            await sslStream.ReadAsync(new byte[1]);

            await sslStream.ShutdownAsync();
        }
    }
}
