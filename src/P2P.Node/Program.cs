using Microsoft.Extensions.Configuration;
using P2P.Node.Models;
using System.Net.Sockets;
using System.Net;
using P2P.Node.Configs;
using P2P.Node.Helpers;
using ChatService = P2P.Node.Services.ChatService;

namespace P2P.Node;

internal class Program
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    static async Task Main(string[] args)
    {
       var nodeName = ConsoleHelper.ReadFromConsoleUntilPredicate("What is your username?", string.IsNullOrEmpty)!.Trim();

        // I need node name to configure log file name. In case if on the same PC I will run multiple nodoes.
        NLogHelper.ConfigureNLog(nodeName);

        // Build a config object, using env vars and JSON providers.
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .AddEnvironmentVariables()
            .Build();

        // Get values from the config given their key and their target type.
        var settings = config.GetRequiredSection(nameof(Settings)).Get<Settings>() ??
                       throw new Exception("Configuration couldn't load");

        string? host = null;
        try
        {
            var localhost = await Dns.GetHostEntryAsync(Dns.GetHostName());
            foreach (var ip in localhost.AddressList.Where(x => x.AddressFamily == AddressFamily.InterNetwork))
            {
                var answer = ConsoleHelper.ReadFromConsoleUntilPredicate($"Do you want to use host {ip}? y/n", input => string.IsNullOrEmpty(input) ||
                    (!input.Equals("y", StringComparison.InvariantCultureIgnoreCase) &&
                    !input.Equals("n", StringComparison.InvariantCultureIgnoreCase)));
                
                if (answer?.Equals("y", StringComparison.InvariantCultureIgnoreCase) == true)
                {
                    host = ip.ToString();
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error was thrown when tried to find available IPs");
        }

        if (string.IsNullOrEmpty(host))
        {
            host = ConsoleHelper.ReadFromConsoleUntilPredicate("What is your host?",
                input => string.IsNullOrEmpty(input) && !int.TryParse(input, out _))!.Trim();
        }

        var portStr = ConsoleHelper.ReadFromConsoleUntilPredicate("What is your port?", input => string.IsNullOrEmpty(input) && !int.TryParse(input, out _))!.Trim();
        var port = Convert.ToInt32(portStr);

        Logger.Info($"Start node {nodeName} {host}:{port}");

        var chatService = new ChatService(new AppNode(nodeName, host, port), settings);
        await chatService.StartServerAsync();
        await chatService.StartClientAsync();

        while (true)
        {
        }

        await chatService.StopServerAsync();
    }
}