using Grpc.Core;
using Proto;

namespace P2P.Node.Server;

internal class ChatService : Proto.ChatService.ChatServiceBase
{
    public bool ChatInProgress { get; private set; } = false;

    public delegate void ChatHandler(string receivedMessage, string chatId);
    public event ChatHandler? OnChatRequest;

    public override Task<ChatResponse> Chat(ChatRequest request, ServerCallContext context)
    {
        ChatInProgress = true;
        OnChatRequest?.Invoke(request.Text, request.ChatId);
        return Task.FromResult(new ChatResponse { IsOk = true });
    }
}