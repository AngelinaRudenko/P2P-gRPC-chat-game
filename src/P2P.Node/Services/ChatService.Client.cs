using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using P2P.Node.Configs;
using P2P.Node.Models;
using Proto;

namespace P2P.Node.Services;

internal partial class ChatService : IDisposable
{
    private readonly AppNode _currentNode;
    private DateTime _startTimestamp;
    
    private readonly TimeoutSettings _timeoutSettings;

    private readonly Timer _isNextNodeAliveTimer;
    private ChatRequest? _lastChatRequest;

    public ChatService(AppNode currentNode, Settings settings)
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

        ConsoleHelper.LogTopology(_chainController.Topology);

        _chainController.OnLeaderElection += ElectLeader;
        _chainController.OnLeaderElectionResult += SendLeaderElectionRequest;

        _chainController.OnStartChat += StartChat;
        _chainController.OnChat += Chat;
        _chainController.OnChatResults += ChatResults;

        _isNextNodeAliveTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(_timeoutSettings.IsAliveTimerPeriod));
    }

    private void IsNextNodeAlive(object? stateInfo)
    {
        if (_chainController.NextNodeChannel != null && IsAliveAsync(_chainController.NextNodeChannel).Result)
        {
            return;
        }

        ConsoleHelper.Debug("Next node is not alive");
        
        _isNextNodeAliveTimer.Change(Timeout.Infinite, Timeout.Infinite);
       
        var previousNode = _chainController.Topology.PreviousNode;
        if (!TryConnectToNextNodeAutomatically().Result)
        {
            ConnectToNextNodeManuallyAsync().Wait();
        }
        _chainController.Topology.PreviousNode = previousNode;

        // disconnected node might be leader (or next next, or next next next...)
        ElectLeader();

        ConsoleHelper.LogTopology(_chainController.Topology);

        if (_lastChatRequest != null)
        {
            var client = new ChainService.ChainServiceClient(_chainController.NextNodeChannel);
            // resend message, do not wait
            client.ChatAsync(_lastChatRequest, deadline: DateTime.UtcNow.AddSeconds(_timeoutSettings.CommonRequestTimeout));
        }

        _isNextNodeAliveTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(_timeoutSettings.IsAliveTimerPeriod));
    }
    
    private async Task<bool> IsAliveAsync(GrpcChannel channel)
    {
        try
        {
            return await channel.ConnectAsync()
                .WaitAsync(TimeSpan.FromSeconds(_timeoutSettings.IsAliveRequestTimeout))
                .ContinueWith(task => task.Status == TaskStatus.RanToCompletion);
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
            AppNode nextNode;

            try
            {
                //Console.WriteLine("Write host of the node you want to connect");
                //var host = Convert.ToString(Console.ReadLine());
                var host = _currentNode.Host;
                Console.WriteLine("Write port of the node you want to connect");
                var port = Convert.ToInt32(Console.ReadLine());
                
                nextNode = new AppNode("unknown", host, port);
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
        if (_chainController.Topology.NextNextNode == null)
            return false;

        ConsoleHelper.Debug($"Try to automatically connect to next next node {_chainController.Topology.NextNextNode.Name}");
        return await TryConnectToNextNode(_chainController.Topology.NextNextNode);
    }

    private async Task<bool> TryConnectToNextNode(AppNode nextNode)
    {
        var nextNodeChannel = GrpcChannel.ForAddress(nextNode.ToString(),
            new GrpcChannelOptions { Credentials = ChannelCredentials.Insecure });

        try
        {
            var nextNodeClient = new ChainService.ChainServiceClient(nextNodeChannel);

            var connectResponse = await nextNodeClient.ConnectAsync(
                new ConnectRequest { NodeWantsToConnect = SingletonMapper.Map<AppNode, Proto.Node>(_currentNode) },
                deadline: DateTime.UtcNow.AddSeconds(_timeoutSettings.ConnectRequestTimeout));

            if (!connectResponse.IsOk)
            {
                throw new Exception($"Failed to connect to node {nextNode}");
            }

            ConsoleHelper.WriteGreen($"Connected to node {connectResponse.Topology.NextNode.Name}");

            _chainController.Topology = SingletonMapper.Map<Topology, AppTopology>(connectResponse.Topology);
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

    #region Leader election

    public void ElectLeader()
    {
        ElectLeader(new LeaderElectionRequest
        {
            ElectionLoopId = Guid.NewGuid().ToString(),
            LeaderNode = SingletonMapper.Map<AppNode, Proto.Node>(_currentNode),
            LeaderConnectionTimestamp = Timestamp.FromDateTime(_startTimestamp)
        });
    }

    public void ElectLeader(LeaderElectionRequest request)
    {
        // current node started earlier than assumed leader
        if (request.LeaderConnectionTimestamp.ToDateTime() > _startTimestamp)
        {
            request.LeaderNode = SingletonMapper.Map<AppNode, Proto.Node>(_currentNode);
            request.LeaderConnectionTimestamp = Timestamp.FromDateTime(_startTimestamp);
        }

        SendLeaderElectionRequest(request);
    }
    
    public void SendLeaderElectionRequest(LeaderElectionRequest request)
    {
        // do not wait
        var client = new ChainService.ChainServiceClient(_chainController.NextNodeChannel);
        client.ElectLeaderAsync(request, deadline: DateTime.UtcNow.AddSeconds(_timeoutSettings.CommonRequestTimeout)); 
    }

    #endregion

    #region Chat

    /// <summary>
    /// Leader starts new chat game
    /// </summary>
    public void StartChat()
    { 
        if (_currentNode.Equals(_chainController.Topology.NextNode)) 
        { 
            Console.WriteLine("Game is stopped, you're the only one player"); 
            return;
        }
        
        _chainController.OnStartChat -= StartChat;

        if (_chainController.ChatInProgress || !_currentNode.Equals(_chainController.Topology.Leader))
        {
            Console.WriteLine("Game is in progress, wait for your turn");
            return;
        }

        _chainController.ChatInProgress = true;
        _chainController.ChatId = Guid.NewGuid().ToString();

        Console.WriteLine("Start new game, write the message for the next player");
        var input = Console.ReadLine();

        _lastChatRequest = new ChatRequest
        {
            ChatId = _chainController.ChatId,
            Message = input,
            MessageChain = $"{_currentNode.Name}: {input}"
        };

        SendChatRequest(_lastChatRequest);
    }

    /// <summary>
    /// Non-leader node play chat game
    /// </summary>
    /// <param name="request">Message received from previous node</param>
    public void Chat(ChatRequest request)
    {
        Console.WriteLine($"Previous player said '{request.Message}'. Write message to next player:");
        var input = Console.ReadLine();

        request.Message = input;
        request.MessageChain = $"{request.MessageChain}\n{_currentNode.Name}: {input}";
        _lastChatRequest = request;

        SendChatRequest(request);
    }

    /// <summary>
    /// Propagate chat game results when game is finished
    /// </summary>
    /// <param name="request">Request contains messages from all players</param>
    public void ChatResults(ChatRequest request)
    {
        Console.WriteLine("Chat results:");
        Console.WriteLine(request.MessageChain);
       
        var client = new ChainService.ChainServiceClient(_chainController.NextNodeChannel);
        // do not wait
        client.ChatAsync(request, deadline: DateTime.UtcNow.AddSeconds(_timeoutSettings.CommonRequestTimeout));

        _chainController.OnStartChat += StartChat;

        if (_currentNode.Equals(_chainController.Topology.Leader))
        {
            _startTimestamp = DateTime.UtcNow; // put to the end of the front
            ElectLeader(); // next leader will be the one, who connected after the current leader
        }
    }

    private void SendChatRequest(ChatRequest request)
    {
        // do not wait
        var client = new ChainService.ChainServiceClient(_chainController.NextNodeChannel);
        client.ChatAsync(request, deadline: DateTime.UtcNow.AddSeconds(_timeoutSettings.CommonRequestTimeout));
    }

    #endregion

    public void Dispose()
    {
        _isNextNodeAliveTimer.Dispose();
        StopServerAsync().Wait();
    }
}