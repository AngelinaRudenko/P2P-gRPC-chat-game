using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using P2P.Node.Configs;
using P2P.Node.Helpers;
using P2P.Node.Models;
using Proto;

namespace P2P.Node.Services;

internal partial class ChatService : IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly AppNode _currentNode;
    private DateTime _startTimestamp;

    private readonly TimeoutSettings _timeoutSettings;

    private readonly Timer _isNextNodeAliveTimer;
    private ChatRequest? _lastChatRequest;
    private bool _typingMessage;

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

        NLogHelper.LogTopology(Logger, _chainController.Topology);

        _chainController.OnLeaderElection += ElectLeader;
        _chainController.OnLeaderElectionResult += SendLeaderElectionRequest;

        _chainController.OnStartChat += StartChat;
        _chainController.OnChat += Chat;

        _isNextNodeAliveTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(_timeoutSettings.IsAliveTimerPeriod));
    }

    private void IsNextNodeAlive(object? stateInfo)
    {
        if (_chainController.Topology.NextNode != null && IsAliveAsync(_chainController.Topology.NextNode.Channel.Value).Result)
        {
            return;
        }

        Logger.Debug("Next node is not alive");

        _isNextNodeAliveTimer.Change(Timeout.Infinite, Timeout.Infinite);

        if (!TryConnectToNextNodeAutomatically().Result)
        {
            ConnectToNextNodeManuallyAsync().Wait();
        }

        SetNextNextNode(_chainController.Topology.PreviousNode!, _chainController.Topology.NextNode!).Wait();

        // disconnected node might be leader (or next next, or next next next...)
        ElectLeader();

        NLogHelper.LogTopology(Logger, _chainController.Topology);

        if (_lastChatRequest != null)
        {
            Logger.Debug("Resend last message again");
            SendChatRequest(_lastChatRequest); // resend message
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
                var host = ConsoleHelper.ReadFromConsoleUntilPredicate("Write host of the node you want to connect",
                    input => string.IsNullOrEmpty(input) && !int.TryParse(input, out _))!.Trim();
                 //var host = _currentNode.Host;

                var portStr = ConsoleHelper.ReadFromConsoleUntilPredicate("Write port of the node you want to connect", 
                    input => string.IsNullOrEmpty(input) && !int.TryParse(input, out _))!.Trim();

                var port = Convert.ToInt32(portStr);

                nextNode = new AppNode("unknown", host, port);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Invalid input: {ex.Message}");
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

        Logger.Debug($"Try to automatically connect to next next node {_chainController.Topology.NextNextNode.Name}");
        return await TryConnectToNextNode(_chainController.Topology.NextNextNode);
    }

    private async Task<bool> TryConnectToNextNode(AppNode nextNode)
    {
        try
        {
            var nextNodeClient = new ChainService.ChainServiceClient(nextNode.Channel.Value);

            var connectResponse = await nextNodeClient.ConnectAsync(
                new ConnectRequest { NodeWantsToConnect = SingletonMapper.Map<AppNode, Proto.Node>(_currentNode) },
                deadline: DateTime.UtcNow.AddSeconds(_timeoutSettings.ConnectRequestTimeout));

            if (!connectResponse.IsOk)
            {
                throw new Exception($"Failed to connect to node {nextNode}");
            }

            Logger.Info($"Connected to node {connectResponse.Topology.NextNode.Name}");

            var previousNode = _chainController.Topology.PreviousNode;
            _chainController.Topology = SingletonMapper.Map<Topology, AppTopology>(connectResponse.Topology);
            _chainController.Topology.PreviousNode ??= previousNode;

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Failed ot connect to node {nextNode.Name}: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> SetNextNextNode(AppNode recipientNode, AppNode nextNextNode)
    {
        try
        {
            Logger.Debug($"Try to set next next {nextNextNode.Name} for node {recipientNode.Name}");

            var recipientNodeClient = new ChainService.ChainServiceClient(recipientNode.Channel.Value);

            var response = await recipientNodeClient.SetNextNextNodeAsync(
                new SetNextNextNodeRequest { NextNextNode = SingletonMapper.Map<AppNode, Proto.Node>(nextNextNode) },
            deadline: DateTime.UtcNow.AddSeconds(_timeoutSettings.CommonRequestTimeout));

            if (!response.IsOk)
            {
                Logger.Debug($"Failed to set next next {nextNextNode.Name} for node {recipientNode.Name}");
                return false;
            }

            Logger.Debug($"Successfully set next next {nextNextNode.Name} for node {recipientNode.Name}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Failed to set next next for node {recipientNode.Name} ({recipientNode}): {ex.Message}");
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
        var client = new ChainService.ChainServiceClient(_chainController.Topology.NextNode!.Channel.Value);
        client.ElectLeaderAsync(request, deadline: DateTime.UtcNow.AddSeconds(_timeoutSettings.CommonRequestTimeout));
    }

    #endregion

    #region Chat

    public void StartChat()
    {
        if (_currentNode.Equals(_chainController.Topology.NextNode))
        {
            _lastChatRequest = null;
            Console.WriteLine("Game is stopped, you're the only one player");
            return;
        }

        if (_lastChatRequest != null && !_typingMessage && (_lastChatRequest?.ChatStatus != ChatStatus.Propagation || !_currentNode.Equals(_chainController.Topology.Leader)))
        {
            Console.WriteLine("Game is in progress, wait for your turn");
            return;
        }

        _typingMessage = true;

        var input = ConsoleHelper.ReadFromConsoleUntilPredicate("Start new game, write the message for the next player", string.IsNullOrEmpty);

        _lastChatRequest = new ChatRequest
        {
            ChatId = Guid.NewGuid().ToString(),
            Message = input,
            MessageChain = $"{_currentNode.Name}: {input}",
            ChatStatus = ChatStatus.InProgress
        };

        Logger.Info($"Start new game with ChatId: {_lastChatRequest.ChatId}");
        SendChatRequest(_lastChatRequest);
        _typingMessage = false;
    }

    public void Chat(ChatRequest request)
    {
        if (_typingMessage)
        {
            Logger.Debug("Already typing for other game, skip received mesasge");
            return;
        }

        var isResultPropagation = request.ChatStatus == ChatStatus.Propagation || request.ChatId.Equals(_lastChatRequest?.ChatId);
        Logger.Trace($"Received request with ChatId: {request.ChatId} and status {request.ChatStatus}. Last request ChatId {_lastChatRequest?.ChatId}.");

        if (isResultPropagation)
        {
            if (_currentNode.Equals(_chainController.Topology.Leader))
            {
                _startTimestamp = DateTime.UtcNow; // put to the end of the front
            }

            if (_lastChatRequest?.ChatStatus == ChatStatus.Propagation)
            {
                Logger.Debug("Propagation loop finished, elect new leader");
                // current node was the one who started propagation loop
                ElectLeader(); // next leader will be the one, who connected after the current leader
            }
            else
            {
                request.ChatStatus = ChatStatus.Propagation;
                Logger.Debug("Propagate results");
                Logger.Info($"Chat results:\n{request.MessageChain}");

                SendChatRequest(request);
            }
        }
        else
        {
            var input = ConsoleHelper.ReadFromConsoleUntilPredicate($"Previous player said '{request.Message}'. Write message to next player:", string.IsNullOrEmpty);

            request.Message = input;
            request.MessageChain = $"{request.MessageChain}\n{_currentNode.Name}: {input}";

            SendChatRequest(request);
        }

        _lastChatRequest = request;
        _typingMessage = false;
    }

    private void SendChatRequest(ChatRequest request)
    {
        Logger.Trace($"Send request {_lastChatRequest} to node {_chainController.Topology.NextNode?.Name}");
        // do not wait
        var client = new ChainService.ChainServiceClient(_chainController.Topology.NextNode!.Channel.Value);
        client.ChatAsync(request, deadline: DateTime.UtcNow.AddSeconds(_timeoutSettings.CommonRequestTimeout));
    }

    #endregion

    public void Dispose()
    {
        _isNextNodeAliveTimer.Dispose();
        StopServerAsync().Wait();
    }
}