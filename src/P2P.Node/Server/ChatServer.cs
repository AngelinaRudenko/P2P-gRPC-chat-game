using Grpc.Core;
using P2P.Node.Services;

namespace P2P.Node.Server;

public class ChatServer
{
    private Server? _server;
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

    public async Task Start()
    {
        try
        {
            var chainService = new ChainService();
            chainService.OnDisconnectRequest += InvokeDisconnect;

            _server = new Server
            {
                Services =
                {
                    Proto.ChatService.BindService(new ChatService()),
                    Proto.ChainService.BindService(chainService)
                },
                Ports = { new ServerPort(_host, _port, ServerCredentials.Insecure) }
            };

            _server.Start();
            Console.WriteLine($"Server is listening on port {_host}:{_port}");
        }
        catch (IOException e)
        {
            Console.WriteLine($"Server failed to start: {e.Message}");
            await Stop();
            throw;
        }
    }

    public async Task Stop()
    {
        if (_server != null)
        {
            Console.WriteLine($"Server shutdown");
            await _server.ShutdownAsync();
        }
    }
}