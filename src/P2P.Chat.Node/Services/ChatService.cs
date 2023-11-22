using Grpc.Core;
using Proto;

namespace P2P.Chat.Node.Services;

public class ChatService : Proto.ChatService.ChatServiceBase
{
    public override Task<ChatResponse> Chat(ChatRequest request, ServerCallContext context)
    {
        return Task.FromResult(new ChatResponse { IsOk = true });
    }
}