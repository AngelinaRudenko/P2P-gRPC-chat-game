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
                // TODO: that's bad, find other solution
                Console.WriteLine($"Previous player said '{request.Text}'");
            });

            return Task.FromResult(new ChatResponse { IsOk = true });
        }
    }
}
