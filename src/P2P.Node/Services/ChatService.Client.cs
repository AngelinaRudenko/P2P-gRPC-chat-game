using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using P2P.Node.Models;
using Proto;

namespace P2P.Node.Services;

internal partial class ChatService : IDisposable
{
    private readonly Proto.Node _currentNode;
    private DateTime _startTimestamp;
    
    private readonly TimeoutSettings _timeoutSettings;

    private readonly Timer _isNextNodeAliveTimer;

    public ChatService(Proto.Node currentNode, Settings settings)
    {
        _currentNode = currentNode;
        _startTimestamp = DateTime.UtcNow;
        _timeoutSettings = settings.TimeoutSettings;

        _chainController = new Server.ChainService(_currentNode, _timeoutSettings);

        _isNextNodeAliveTimer = new Timer(IsNextNodeAlive, null, Timeout.Infinite, Timeout.Infinite);
    }

    public async Task StartClientAsync()
    {
        await ConnectToNextNodeManuallyAsync();

        if (_chainController.Topology.Leader == null)
        {
            ElectLeader();
        }

        var topology = _chainController.Topology;
        ConsoleHelper.LogTopology(topology);

        _chainController.OnLeaderElection += ElectLeader;
        _chainController.OnLeaderElectionResult += PropagateElectedLeader;

        _chainController.OnLeaderElectionResult += StartChat;
        _chainController.OnChat += Chat;
        _chainController.OnChatResults += ChatResults;

        _isNextNodeAliveTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(_timeoutSettings.IsAliveTimerPeriod)); // check is alive status every 5 sec
    }


    private void IsNextNodeAlive(object? stateInfo)
    {
        if (_chainController.NextNodeChannel == null || !IsAliveAsync(_chainController.NextNodeChannel).Result)
        {
            _isNextNodeAliveTimer.Change(Timeout.Infinite, Timeout.Infinite);

            ConsoleHelper.Debug("Next node is not alive");
            var previousNode = _chainController.Topology.PreviousNode;
            if (!TryConnectToNextNodeAutomatically().Result)
            {
                ConnectToNextNodeManuallyAsync().Wait();
            }
            _chainController.Topology.PreviousNode = previousNode;

            // disconnected node might be leader (or next next, or next next next...)
            ElectLeader();

            var topology = _chainController.Topology;
            ConsoleHelper.LogTopology(topology);

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

    private async Task ConnectToNextNodeManuallyAsync()
    {
        while (true)
        {
            Proto.Node nextNode;

            try
            {
                //Console.WriteLine("Write host of the node you want to connect");
                //var host = Convert.ToString(Console.ReadLine());
                var host = _currentNode.Host;
                Console.WriteLine("Write port of the node you want to connect");
                var port = Convert.ToInt32(Console.ReadLine());
                
                nextNode = new Proto.Node { Host = host, Port = port };
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteRed($"Invalid input: {ex.Message}");
                continue;
            }

            if (await TryConnectToNextNode(nextNode))
            {
                break;
            }
        }
    }

    private async Task<bool> TryConnectToNextNodeAutomatically()
    {
        ConsoleHelper.Debug("Try to automatically connect to next next node");
        return _chainController.Topology.NextNextNode != null && await TryConnectToNextNode(_chainController.Topology.NextNextNode);
    }

    private async Task<bool> TryConnectToNextNode(Proto.Node nextNode)
    {
        var nextNodeChannel = GrpcChannel.ForAddress($"http://{nextNode.Host}:{nextNode.Port}",
            new GrpcChannelOptions { Credentials = ChannelCredentials.Insecure });

        try
        {
            var nextNodeClient = new ChainService.ChainServiceClient(nextNodeChannel);

            var connectResponse = await nextNodeClient.ConnectAsync(
                new ConnectRequest { NodeWantsToConnect = _currentNode },
                deadline: DateTime.UtcNow.AddSeconds(_timeoutSettings.ConnectRequestTimeout));

            if (!connectResponse.IsOk)
            {
                throw new Exception($"Failed to connect to node http://{nextNode.Host}:{nextNode.Port}");
            }

            ConsoleHelper.WriteGreen($"Connected to node {connectResponse.Topology.NextNode.Name}");

            _chainController.Topology = connectResponse.Topology;
            _chainController.NextNodeChannel = nextNodeChannel;

            return true;
        }
        catch (Exception ex)
        {
            await nextNodeChannel.ShutdownAsync();
            ConsoleHelper.WriteRed($"Failed ot connect to node {nextNode.Name}: {ex.Message}");
            return false;
        }
    }

    public void ElectLeader()
    {
        ElectLeader(new LeaderElectionRequest
        {
            ElectionLoopId = Guid.NewGuid().ToString(),
            LeaderNode = _currentNode,
            LeaderConnectionTimestamp = Timestamp.FromDateTime(_startTimestamp)
        });
    }

    public void ElectLeader(LeaderElectionRequest request)
    {
        var client = new ChainService.ChainServiceClient(_chainController.NextNodeChannel);

        // current node started earlier than assumed leader
        if (request.LeaderConnectionTimestamp.ToDateTime() > _startTimestamp)
        {
            request.LeaderNode = _currentNode;
            request.LeaderConnectionTimestamp = Timestamp.FromDateTime(_startTimestamp);
        }

        client.ElectLeaderAsync(request, deadline: DateTime.UtcNow.AddSeconds(_timeoutSettings.CommonRequestTimeout)); // do not wait
    }

    public void PropagateElectedLeader(LeaderElectionRequest request)
    {
        var client = new ChainService.ChainServiceClient(_chainController.NextNodeChannel);
        client.ElectLeaderAsync(request, deadline: DateTime.UtcNow.AddSeconds(_timeoutSettings.CommonRequestTimeout)); // do not wait
    }

    public void StartChat(LeaderElectionRequest request)
    {
        _chainController.OnLeaderElectionResult -= StartChat;

        if (_chainController.ChatInProgress || _chainController.Topology.Leader?.Host != _currentNode.Host 
                                            || _chainController.Topology.Leader?.Port != _currentNode.Port)
        {
            Console.WriteLine("Game is in progress, wait for your turn");
            return;
        }

        var chatId = Guid.NewGuid().ToString();

        _chainController.ChatInProgress = true;
        _chainController.ChatId = chatId;

        Console.WriteLine("Start new game, write the message for the next player");
        var input = Console.ReadLine();

        var client = new ChainService.ChainServiceClient(_chainController.NextNodeChannel);
        // do not wait
        client.ChatAsync(
            new ChatRequest
            {
                StartedByNode = _currentNode,
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

        var client = new ChainService.ChainServiceClient(_chainController.NextNodeChannel);
        // do not wait
        client.ChatAsync(request, deadline: DateTime.UtcNow.AddSeconds(_timeoutSettings.CommonRequestTimeout));
    }

    public void ChatResults(ChatRequest request)
    {
        Console.WriteLine("Chat results:");
        Console.WriteLine(request.MessageChain);
       
        var client = new ChainService.ChainServiceClient(_chainController.NextNodeChannel);
        // do not wait
        client.ChatAsync(request, deadline: DateTime.UtcNow.AddSeconds(_timeoutSettings.CommonRequestTimeout));

        _chainController.OnLeaderElectionResult += StartChat;

        if (request.StartedByNode.Host == _currentNode.Host && request.StartedByNode.Port == _currentNode.Port)
        {
            _startTimestamp = DateTime.UtcNow; // put to the end of the front
            ElectLeader(); // next leader will be the one, who connected after the current leader
        }
    }

    public void Dispose()
    {
        _isNextNodeAliveTimer.Dispose();
        _server?.ShutdownAsync().Wait();
    }
}