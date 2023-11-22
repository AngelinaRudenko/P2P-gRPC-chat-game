using Grpc.Core;
using Grpc.Net.Client;
using Proto;
using ChatService = P2P.Node.Services.ChatService;

namespace P2P.Node
{
    internal class Program
    {
        private const int Port = 50051;

        static async Task Main(string[] args)
        {
            var chatServer = new ChatServer(Port);
            await chatServer.Start();

            //var channel = new Channel("127.0.0.1:50051", ChannelCredentials.Insecure);
            var channel = GrpcChannel.ForAddress("http://localhost:50051", 
                new GrpcChannelOptions{ Credentials = ChannelCredentials.Insecure });

            await channel.ConnectAsync().ContinueWith((task) =>
            {
                if (task.Status == TaskStatus.RanToCompletion)
                {
                    Console.WriteLine("You successfully connected to server");
                }
            });

            var input = string.Empty;
            while (input != "/q")
            {
                input = Console.ReadLine();

                var client = new Proto.ChatService.ChatServiceClient(channel);
                var result = await client.ChatAsync(new ChatRequest { Text = input });
                Console.WriteLine(result.IsOk);
            }

            await chatServer.Stop();
        }
    }
}