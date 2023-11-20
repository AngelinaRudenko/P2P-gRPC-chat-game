using Grpc.Core;
using Proto;

namespace P2P.Node.Services
{
    internal class ChatService : Proto.ChatService.ChatServiceBase
    {
        public override Task<ChatResponse> Chat(ChatRequest request, ServerCallContext context)
        {
            Task.Run(() =>
            {
                Console.WriteLine($"Previous player said '{request.Text}'");
                Console.WriteLine("Your turn, rephrase the text:");
                var currentPlayerText = Console.ReadLine();
            });

            return Task.FromResult(new ChatResponse { IsOk = true });
        }
    }
}
