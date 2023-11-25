using Grpc.Core;
using P2P.Node.Services;

namespace P2P.Node.Server;

public class ChatServer
{
    private Grpc.Core.Server? _server;
    private readonly string _host;
    private readonly int _port;

    public event Action? OnDisconnectRequest;

    public ChatServer(string host, int port)
    {
        _host = host;
        _port = port;
    }

    private void InvokeDisconnect()
    {
        OnDisconnectRequest?.Invoke();
    }

    public async Task StartAsync()
    {
        try
        {
            var chainService = new ChainService();
            chainService.OnDisconnectRequest += InvokeDisconnect;

            _server = new Grpc.Core.Server
            {
                Services =
                {
                    Proto.ChatService.BindService(new ChatService()),
                    Proto.ChainService.BindService(chainService)
                },
                Ports = { new ServerPort(_host, _port, ServerCredentials.Insecure) }
            };

            _server.Start();
            ConsoleHelper.Debug($"Server is listening on port {_host}:{_port}");
        }
        catch (IOException e)
        {
            ConsoleHelper.WriteRed($"Server failed to start: {e.Message}");
            await StopAsync();
            throw;
        }
    }

    public async Task StopAsync()
    {
        if (_server != null)
        {
            ConsoleHelper.Debug("Server shutdown");
            await _server.ShutdownAsync();
        }
    }
}