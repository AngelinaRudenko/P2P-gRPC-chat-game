using Grpc.Core;
using Proto;

namespace P2P.Node.Server;

internal class ChatService : Proto.ChatService.ChatServiceBase
{
    public bool ChatInProgress { get; private set; }
    private string _chatId = string.Empty;

    public delegate void ChatHandler(string chatId, string receivedMessage, string messageChain);
    public event ChatHandler? OnChat;
    public delegate void ChatResultsHandler(string chatId, string messageChain);
    public event ChatResultsHandler? OnChatResults;

    public override Task<ChatResponse> Chat(ChatRequest request, ServerCallContext context)
    {
        if (_chatId.Equals(request.ChatId, StringComparison.InvariantCulture) == false)
        {
            ChatInProgress = true;
            _chatId = request.ChatId;
            OnChat?.Invoke(request.ChatId, request.Message,  request.MessageChain);
        }
        else if (ChatInProgress)
        {
            // chat loop finished, propagate results
            ChatInProgress = false;
            OnChatResults?.Invoke(request.ChatId, request.MessageChain);
        }
        
        return Task.FromResult(new ChatResponse { IsOk = true });
    }
}