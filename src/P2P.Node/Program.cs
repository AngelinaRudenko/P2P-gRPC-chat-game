using Microsoft.Extensions.Configuration;
using P2P.Node.Models;
using System.Net.Sockets;
using System.Net;
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

        Console.WriteLine("What is your username?");
        var name = Console.ReadLine();

        string? host = null;
        int? port = 7654;
        try
        {
            var localhost = await Dns.GetHostEntryAsync(Dns.GetHostName());
            var ip = localhost.AddressList.FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork);
            if (ip != null)
            {
                host = ip.ToString();
            }
        }
        catch
        {
            // ignore
        }

        if (host == null)
        {
            Console.WriteLine("Write your node host");
            host = Convert.ToString(Console.ReadLine());
            //Console.WriteLine("Write your node port");
            //port = Convert.ToInt32(Console.ReadLine());
        }

        Console.WriteLine("Write your node port");
        port = Convert.ToInt32(Console.ReadLine());

        var chatService = new ChatService(new AppNode(name, host, port.Value), settings);
        await chatService.StartServerAsync();
        await chatService.StartClientAsync();

        while (true)
        {
        }

        await chatService.StopServerAsync();
    }
}