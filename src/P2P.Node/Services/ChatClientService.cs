using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using P2P.Node.Models;
using Proto;

namespace P2P.Node.Services;

internal class ChatClientService : IDisposable
{
    private DateTime _startTimestamp;
    private readonly int _currentNodeId;
    private readonly NodeSettings[] _nodes;
    private GrpcChannel? _nextNodeChannel;
    private readonly TimeoutSettings _timeoutSettings;
    private readonly Server.ChainService _chainService;
    private readonly Server.ChatService _chatService;

    private readonly Timer _isNextNodeAliveTimer;

    public ChatClientService(Settings settings, Server.ChainService chainService, Server.ChatService chatService)
    {
        _startTimestamp = DateTime.UtcNow;
        _currentNodeId = settings.CurrentNodeId;
        _nodes = settings.NodesSettings;
        _timeoutSettings = settings.TimeoutSettings;
        _chainService = chainService;
        _chatService = chatService;

        _isNextNodeAliveTimer = new Timer(IsNextNodeAlive, null, Timeout.Infinite, Timeout.Infinite);
    }

    public async Task StartAsync()
    {
        await EstablishConnectionAsync();

        _chainService.OnDisconnect += Disconnect;
        _chainService.OnLeaderElection += ElectLeader;
        _chainService.OnLeaderElectionResult += PropagateElectedLeader;

        _chainService.OnLeaderElectionResult += StartChat;
        _chatService.OnChat += Chat;
        _chatService.OnChatResults += ChatResults;

        _isNextNodeAliveTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(_timeoutSettings.IsAliveTimerPeriod)); // check is alive status every 5 sec
    }

    private static int GetNextNodeId(int id, int nodesCount)
    {
        return id + 1 == nodesCount ? 0 : ++id;
    }


    private void IsNextNodeAlive(object? stateInfo)
    {
        if (_nextNodeChannel == null || !IsAliveAsync(_nextNodeChannel).Result)
        {
            _isNextNodeAliveTimer.Change(Timeout.Infinite, Timeout.Infinite);

            ConsoleHelper.Debug("Reconnect");
            EstablishConnectionAsync().Wait();

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

    private async Task EstablishConnectionAsync()
    {
        var nextNodeId = _currentNodeId;
        while (true)
        {
            nextNodeId = GetNextNodeId(nextNodeId, _nodes.Length);

            if (nextNodeId == _currentNodeId)
            {
                ConsoleHelper.Debug($"Couldn't connect to any node. Sleep for {_timeoutSettings.ReestablishConnectionPeriod} sec");
                await Task.Delay(TimeSpan.FromSeconds(_timeoutSettings.ReestablishConnectionPeriod));
                continue;
            }

            var nextNodeChannel = GrpcChannel.ForAddress(
                _nodes[nextNodeId].ToString(),
                new GrpcChannelOptions { Credentials = ChannelCredentials.Insecure });

            if (!await IsAliveAsync(nextNodeChannel))
            {
                ConsoleHelper.Debug($"Node {nextNodeId} is not alive");
                continue;
            }

            ConsoleHelper.Debug($"Node {nextNodeId} is alive");

            try
            {
                var nextNodeClient = new ChainService.ChainServiceClient(nextNodeChannel);

                var askPermissionResult = await nextNodeClient.AskPermissionToConnectAsync(
                    new AskPermissionToConnectRequest { NodeWantsToConnectId = _currentNodeId },
                    deadline: DateTime.UtcNow.AddSeconds(_timeoutSettings.CommonRequestTimeout));

                if (!askPermissionResult.CanConnect)
                {
                    var previousNodeChannel = GrpcChannel.ForAddress(
                        _nodes[askPermissionResult.ConnectedNodeId].ToString(),
                        new GrpcChannelOptions { Credentials = ChannelCredentials.Insecure });

                    if (await IsAliveAsync(previousNodeChannel))
                    {
                        var previousNodeClient = new ChainService.ChainServiceClient(previousNodeChannel);

                        var askToDisconnectResult = await previousNodeClient.AskToDisconnectAsync(
                            new AskToDisconnectRequest { NodeAsksToDiconnectId = _currentNodeId },
                            deadline: DateTime.UtcNow.AddSeconds(_timeoutSettings.DisconnectRequestTimeout));

                        if (!askToDisconnectResult.IsOk)
                        {
                            throw new Exception("Node doesn't agree to disconnect");
                        }
                    } 

                    await previousNodeChannel.ShutdownAsync();
                }

                var connectResult = await nextNodeClient.ConnectAsync(
                    new ConnectRequest { NodeWantsToConnectId = _currentNodeId },
                    deadline: DateTime.UtcNow.AddSeconds(_timeoutSettings.CommonRequestTimeout));

                if (!connectResult.IsOk)
                {
                    throw new Exception($"Failed to connect to node {nextNodeId}");
                }

                ConsoleHelper.WriteGreen($"Connected to node {nextNodeId} http://{_nodes[nextNodeId].Host}:{_nodes[nextNodeId].Port}");

                _nextNodeChannel = nextNodeChannel;
                break;
            }
            catch
            {
                await nextNodeChannel.ShutdownAsync();
                ConsoleHelper.WriteRed($"Failed ot connect to node {nextNodeId} http://{_nodes[nextNodeId].Host}:{_nodes[nextNodeId].Port}");
            }
        }

        ElectLeader();
    }

    public void ElectLeader()
    {
        ElectLeader(new LeaderElectionRequest
        {
            ElectionLoopId = Guid.NewGuid().ToString(),
            LeaderId = _currentNodeId,
            LeaderConnectionTimestamp = Timestamp.FromDateTime(_startTimestamp)
        });
    }

    public void ElectLeader(LeaderElectionRequest request)
    {
        var client = new ChainService.ChainServiceClient(_nextNodeChannel);

        // current node started earlier than assumed leader
        if (request.LeaderConnectionTimestamp.ToDateTime() > _startTimestamp)
        {
            request.LeaderId = _currentNodeId;
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
        _chainService.OnLeaderElectionResult -= StartChat;

        if (_chatService.ChatInProgress || _chainService.LeaderId != _currentNodeId)
        {
            Console.WriteLine("Game is in progress, wait for your turn");
            return;
        }

        var chatId = Guid.NewGuid().ToString();

        _chatService.ChatInProgress = true;
        _chatService.ChatId = chatId;

        Console.WriteLine("Start new game, write the message for the next player");
        var input = Console.ReadLine();

        var client = new ChatService.ChatServiceClient(_nextNodeChannel);
        // do not wait
        client.ChatAsync(
            new ChatRequest
            {
                StartedByNodeId = _currentNodeId,
                ChatId = chatId,
                Message = input,
                MessageChain = $"{_currentNodeId}: {input}"
            }, 
            deadline: DateTime.UtcNow.AddSeconds(_timeoutSettings.CommonRequestTimeout));
    }

    public void Chat(ChatRequest request)
    {
        Console.WriteLine($"Previous player said '{request.Message}'. Write message to next player:");
        var input = Console.ReadLine();

        request.Message = input;
        request.MessageChain = $"{request.MessageChain}\n{_currentNodeId}: {input}";

        var client = new ChatService.ChatServiceClient(_nextNodeChannel);
        // do not wait
        client.ChatAsync(request, deadline: DateTime.UtcNow.AddSeconds(_timeoutSettings.CommonRequestTimeout));
    }

    public void ChatResults(ChatRequest request)
    {
        Console.WriteLine("Chat results:");
        Console.WriteLine(request.MessageChain);
       
        var client = new ChatService.ChatServiceClient(_nextNodeChannel);
        // do not wait
        client.ChatAsync(request, deadline: DateTime.UtcNow.AddSeconds(_timeoutSettings.CommonRequestTimeout));

        _chainService.OnLeaderElectionResult += StartChat;

        if (request.StartedByNodeId == _currentNodeId)
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