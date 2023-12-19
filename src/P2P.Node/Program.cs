using Microsoft.Extensions.Configuration;
using P2P.Node.Models;
using ChatService = P2P.Node.Services.ChatService;

namespace P2P.Node;

internal class Program
{
    static async Task Main(string[] args)
    {
        // Build a config object, using env vars and JSON providers.
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .AddEnvironmentVariables()
            .Build();

        // Get values from the config given their key and their target type.
        var settings = config.GetRequiredSection(nameof(Settings)).Get<Settings>() ??
                       throw new Exception("Configuration couldn't load");

        Console.WriteLine($"Node {settings.CurrentNodeId}");

        var chatService = new ChatService(settings);
        await chatService.StartServerAsync();
        await chatService.StartClientAsync();

        while (true)
        {
        }

        await chatService.StopServerAsync();
    }
}