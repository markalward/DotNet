using System.Net;
using System.Net.Sockets;

namespace ConnectTimeoutRepro
{
    public class Program
    {
        static void Main(string[] args)
        {
            // Start a TCP server. The server simulates spurious TCP connection drops by receiving data from
            // the client, but not sending any response data.
            var listener = new TcpListener(IPAddress.Any, 5005);
            listener.Start();

            var listenerTasks = new List<Task>();
            for (int i = 0; i < 5; i++)
            {
                var task = AcceptWorker(listener);
                listenerTasks.Add(task);
            }

            // Send requests to the the server using SocketsHttpHandler.
            SendRequestsAsync().GetAwaiter().GetResult();
        }
        
        private static async Task SendRequestsAsync()
        {
            var handler = new SocketsHttpHandler();
            handler.ConnectCallback = ConnectAsync;

            var client = new HttpClient(handler, true);

            while (true)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                
                try
                {
                    Console.WriteLine("Client: Sending request");
                    using var response = await client.GetAsync("https://localhost:5005/", cts.Token);
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("Client: Request timed out");
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Client: Error sending request: {e}");
                }
            }
        }

        private static async Task AcceptWorker(TcpListener listener)
        {
            byte[] buf = new byte[1024];

            while (true)
            {
                try
                {
                    using (var socket = await listener.AcceptSocketAsync())
                    {
                        Console.WriteLine("Server: Accepted connection");

                        while (true)
                        {
                            int bytesRead = await socket.ReceiveAsync(buf.AsMemory(), SocketFlags.None);
                            if (bytesRead == 0)
                            {
                                Console.WriteLine("Server: Connection closed by client");
                                break;
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Server: Exception thrown: {e}");
                }
            }
        }

        private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext connectContext, CancellationToken ct)
        {
            var s = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                Console.WriteLine("Client: Creating new connection");
                await s.ConnectAsync(connectContext.DnsEndPoint, ct);
                return new NetworkStream(s, ownsSocket: true);
            }
            catch
            {
                s.Dispose();
                throw;
            }
        }
    }
}