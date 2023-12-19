using Grpc.Core;

namespace P2P.Node.Services;

internal partial class ChatService
{
    private Grpc.Core.Server? _server;
    private readonly Server.ChainService _chainController;
    private readonly Server.ChatService _chatController;

    public async Task StartServerAsync()
    {
        var currentNodeSettings = _nodes[_currentNodeId];
        try
        {
            _server = new Grpc.Core.Server
            {
                Services =
                {
                    Proto.ChatService.BindService(_chatController),
                    Proto.ChainService.BindService(_chainController)
                },
                Ports = { new ServerPort(currentNodeSettings.Host, currentNodeSettings.Port, ServerCredentials.Insecure) }
            };

            _server.Start();
            ConsoleHelper.Debug($"Server is listening on {currentNodeSettings}");
        }
        catch (IOException e)
        {
            ConsoleHelper.WriteRed($"Server failed to start: {e.Message}");
            await StopServerAsync();
            throw;
        }
    }

    public async Task StopServerAsync()
    {
        if (_server != null)
        {
            ConsoleHelper.Debug("Server shutdown");
            await _server.ShutdownAsync();
        }
    }
}