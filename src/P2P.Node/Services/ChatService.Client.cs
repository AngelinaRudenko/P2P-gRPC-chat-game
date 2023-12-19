using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using P2P.Node.Models;
using Proto;

namespace P2P.Node.Services;

internal partial class ChatService : IDisposable
{
    private AppNode _currentNode;
    private DateTime _startTimestamp;
    private GrpcChannel? _nextNodeChannel;
    private readonly TimeoutSettings _timeoutSettings;

    private readonly Timer _isNextNodeAliveTimer;

    public ChatService(AppNode currentNode, Settings settings)
    {
        _currentNode = currentNode;
        _startTimestamp = DateTime.UtcNow;
        _timeoutSettings = settings.TimeoutSettings;

        _chainController = new Server.ChainService();
        _chatController = new Server.ChatService();

        _isNextNodeAliveTimer = new Timer(IsNextNodeAlive, null, Timeout.Infinite, Timeout.Infinite);
    }

    public async Task StartClientAsync()
    {
        await ConnectToNextNodeAsync();

        _chainController.OnDisconnect += Disconnect;
        _chainController.OnLeaderElection += ElectLeader;
        _chainController.OnLeaderElectionResult += PropagateElectedLeader;

        _chainController.OnLeaderElectionResult += StartChat;
        _chatController.OnChat += Chat;
        _chatController.OnChatResults += ChatResults;

        _isNextNodeAliveTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(_timeoutSettings.IsAliveTimerPeriod)); // check is alive status every 5 sec
    }


    private void IsNextNodeAlive(object? stateInfo)
    {
        if (_nextNodeChannel == null || !IsAliveAsync(_nextNodeChannel).Result)
        {
            _isNextNodeAliveTimer.Change(Timeout.Infinite, Timeout.Infinite);

            ConsoleHelper.Debug("Reconnect");
            ConnectToNextNodeAsync().Wait();

            _isNextNodeAliveTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(_timeoutSettings.IsAliveTimerPeriod));
        }
    }
    
    private async Task<bool> IsAliveAsync(GrpcChannel channel)
    {
        try
        {
            var isAlive = await channel.ConnectAsync()
                .WaitAsync(TimeSpan.FromSeconds(_timeoutSettings.IsAliveRequestTimeout))
                .ContinueWith(task => task.Status == TaskStatus.RanToCompletion);

            return isAlive;
        }
        catch
        {
            return false;
        }
    }

    private async Task ConnectToNextNodeAsync()
    {
        while (true)
        {
            AppNode nextNode;
            GrpcChannel nextNodeChannel;

            try
            {
                Console.WriteLine("Write host of the node you want to connect");
                var host = Convert.ToString(Console.ReadLine());
                Console.WriteLine("Write port of the node you want to connect");
                var port = Convert.ToInt32(Console.ReadLine());

                nextNode = new AppNode(host, port);

                nextNodeChannel = GrpcChannel.ForAddress(nextNode.ToString(),
                    new GrpcChannelOptions { Credentials = ChannelCredentials.Insecure });

                if (!await IsAliveAsync(nextNodeChannel))
                {
                    ConsoleHelper.Debug($"Node {nextNode} is not alive");
                    continue;
                }

                ConsoleHelper.Debug($"Node {nextNode} is alive");
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteRed($"Invalid input: {ex.Message}");
                continue;
            }

            try {

                var nextNodeClient = new ChainService.ChainServiceClient(nextNodeChannel);

                var askPermissionResult = await nextNodeClient.AskPermissionToConnectAsync(
                    new AskPermissionToConnectRequest { NodeWantsToConnect = new Proto.Node { Host = _currentNode.Host, Port = _currentNode.Port } },
                    deadline: DateTime.UtcNow.AddSeconds(_timeoutSettings.CommonRequestTimeout));

                if (!askPermissionResult.CanConnect)
                {
                    var previousNode = new AppNode(askPermissionResult.ConnectedNode.Host, askPermissionResult.ConnectedNode.Port);
                    var previousNodeChannel = GrpcChannel.ForAddress(previousNode.ToString(),
                        new GrpcChannelOptions { Credentials = ChannelCredentials.Insecure });

                    if (await IsAliveAsync(previousNodeChannel))
                    {
                        var previousNodeClient = new ChainService.ChainServiceClient(previousNodeChannel);

                        var askToDisconnectResult = await previousNodeClient.AskToDisconnectAsync(
                            new AskToDisconnectRequest { NodeAsksToDiconnect = new Proto.Node { Host = _currentNode.Host, Port = _currentNode.Port } },
                            deadline: DateTime.UtcNow.AddSeconds(_timeoutSettings.DisconnectRequestTimeout));

                        if (!askToDisconnectResult.IsOk)
                        {
                            throw new Exception("Node doesn't agree to disconnect");
                        }
                    }

                    await previousNodeChannel.ShutdownAsync();
                }

                var connectResult = await nextNodeClient.ConnectAsync(
                    new ConnectRequest { NodeWantsToConnect = new Proto.Node { Host = _currentNode.Host, Port = _currentNode.Port } },
                    deadline: DateTime.UtcNow.AddSeconds(_timeoutSettings.CommonRequestTimeout));

                if (!connectResult.IsOk)
                {
                    throw new Exception($"Failed to connect to node {nextNode}");
                }

                ConsoleHelper.WriteGreen($"Connected to node {nextNode}");

                _nextNodeChannel = nextNodeChannel;
                break;

            }
            catch (Exception ex)
            {
                await nextNodeChannel.ShutdownAsync();
                ConsoleHelper.WriteRed($"Failed ot connect to node {nextNode}: {ex.Message}");
            }
        }

        ElectLeader();
    }

    public void ElectLeader()
    {
        ElectLeader(new LeaderElectionRequest
        {
            ElectionLoopId = Guid.NewGuid().ToString(),
            LeaderNode = new Proto.Node { Host = _currentNode.Host, Port = _currentNode.Port },
            LeaderConnectionTimestamp = Timestamp.FromDateTime(_startTimestamp)
        });
    }

    public void ElectLeader(LeaderElectionRequest request)
    {
        var client = new ChainService.ChainServiceClient(_nextNodeChannel);

        // current node started earlier than assumed leader
        if (request.LeaderConnectionTimestamp.ToDateTime() > _startTimestamp)
        {
            request.LeaderNode = new Proto.Node { Host = _currentNode.Host, Port = _currentNode.Port };
            request.LeaderConnectionTimestamp = Timestamp.FromDateTime(_startTimestamp);
        }

        client.ElectLeaderAsync(request, deadline: DateTime.UtcNow.AddSeconds(_timeoutSettings.CommonRequestTimeout)); // do not wait
    }

    public void PropagateElectedLeader(LeaderElectionRequest request)
    {
        var client = new ChainService.ChainServiceClient(_nextNodeChannel);
        client.ElectLeaderAsync(request, deadline: DateTime.UtcNow.AddSeconds(_timeoutSettings.CommonRequestTimeout)); // do not wait
    }

    public void StartChat(LeaderElectionRequest request)
    {
        _chainController.OnLeaderElectionResult -= StartChat;

        if (_chatController.ChatInProgress || _chainController.Leader?.Host != _currentNode.Host || _chainController.Leader?.Port != _currentNode.Port)
        {
            Console.WriteLine("Game is in progress, wait for your turn");
            return;
        }

        var chatId = Guid.NewGuid().ToString();

        _chatController.ChatInProgress = true;
        _chatController.ChatId = chatId;

        Console.WriteLine("Start new game, write the message for the next player");
        var input = Console.ReadLine();

        var client = new Proto.ChatService.ChatServiceClient(_nextNodeChannel);
        // do not wait
        client.ChatAsync(
            new ChatRequest
            {
                StartedByNodeHost = _currentNode.Host,
                StartedByNodePort = _currentNode.Port,
                ChatId = chatId,
                Message = input,
                MessageChain = $"{_currentNode.Name}: {input}"
            }, 
            deadline: DateTime.UtcNow.AddSeconds(_timeoutSettings.CommonRequestTimeout));
    }

    public void Chat(ChatRequest request)
    {
        Console.WriteLine($"Previous player said '{request.Message}'. Write message to next player:");
        var input = Console.ReadLine();

        request.Message = input;
        request.MessageChain = $"{request.MessageChain}\n{_currentNode.Name}: {input}";

        var client = new Proto.ChatService.ChatServiceClient(_nextNodeChannel);
        // do not wait
        client.ChatAsync(request, deadline: DateTime.UtcNow.AddSeconds(_timeoutSettings.CommonRequestTimeout));
    }

    public void ChatResults(ChatRequest request)
    {
        Console.WriteLine("Chat results:");
        Console.WriteLine(request.MessageChain);
       
        var client = new Proto.ChatService.ChatServiceClient(_nextNodeChannel);
        // do not wait
        client.ChatAsync(request, deadline: DateTime.UtcNow.AddSeconds(_timeoutSettings.CommonRequestTimeout));

        _chainController.OnLeaderElectionResult += StartChat;

        if (request.StartedByNodeHost == _currentNode.Host && request.StartedByNodePort == _currentNode.Port)
        {
            _startTimestamp = DateTime.UtcNow; // put to the end of the front
            ElectLeader(); // next leader will be the one, who connected after the current leader
        }
    }

    public void Disconnect()
    {
        ConsoleHelper.WriteRed("Disconnect from next node");
        _nextNodeChannel?.ShutdownAsync().Wait();
        _nextNodeChannel = null;
        IsNextNodeAlive(null); // re-establish connection
    }

    public void Dispose()
    {
        _nextNodeChannel?.ShutdownAsync().Wait();
        _nextNodeChannel?.Dispose();
        _isNextNodeAliveTimer.Dispose();
    }
}