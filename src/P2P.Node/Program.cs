using Grpc.Core;
using Proto;
using ChatService = P2P.Node.Services.ChatService;

namespace P2P.Node
{
    internal class Program
    {
        private const int Port = 50051;

        static async Task Main(string[] args)
        {
            Task.Run(async () =>
            {
                Server? server = null;

                try
                {
                    server = new Server
                    {
                        Services =
                        {
                            Proto.ChatService.BindService(new ChatService())
                        },
                        Ports = { new ServerPort("localhost", Port, ServerCredentials.Insecure) }
                    };

                    server.Start();
                    Console.WriteLine($"Server is listening on port {Port}");
                    Console.ReadKey();
                }
                catch (IOException e)
                {
                    Console.WriteLine($"Server failed to start: {e.Message}");
                    throw;
                }
                finally
                {
                    if (server != null)
                    {
                        await server.ShutdownAsync();
                    }
                }
            });

            var channel = new Channel("127.0.0.1:50051", ChannelCredentials.Insecure);

            await channel.ConnectAsync().ContinueWith((task) =>
            {
                if (task.Status == TaskStatus.RanToCompletion)
                {
                    Console.WriteLine("Client successfully connected to server");
                }
            });


            var client = new Proto.ChatService.ChatServiceClient(channel);
            var result = await client.ChatAsync(new ChatRequest { Text = "Hello World" });
            Console.WriteLine(result.IsOk);

            Console.ReadKey();
        }
    }
}