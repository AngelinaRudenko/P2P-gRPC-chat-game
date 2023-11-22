using Grpc.Core;
using P2P.Node.Services;

namespace P2P.Node;

public class ChatServer
{
    private Server? _server;
    private readonly int _port;

    public ChatServer(int port)
    {
        _port = port;
    }

    public async Task Start()
    {
        try
        {
            _server = new Server
            {
                Services =
                {
                    Proto.ChatService.BindService(new ChatService())
                },
                Ports = { new ServerPort("localhost", _port, ServerCredentials.Insecure) }
            };

            _server.Start();
            Console.WriteLine($"Server is listening on port {_port}");
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
            await _server.ShutdownAsync();
        }
    }
}