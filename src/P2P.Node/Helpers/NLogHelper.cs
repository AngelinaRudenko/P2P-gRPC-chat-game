using NLog;
using P2P.Node.Models;

namespace P2P.Node.Helpers;

public class NLogHelper
{
    public static void ConfigureNLog(string nodeName)
    {
        LogManager.Setup().LoadConfiguration(builder => {
            builder.ForLogger().FilterMinLevel(LogLevel.Info).WriteToConsole();
            builder.ForLogger().FilterMinLevel(LogLevel.Trace).WriteToFile(fileName: $"{nodeName}-log.log");
        });
    }

    internal static void LogTopology(ILogger logger, AppTopology topology)
    {
        logger.Info($"Previous {topology.PreviousNode?.Name}, next {topology.NextNode?.Name}," +
                     $" next next {topology.NextNextNode?.Name}, leader {topology.Leader?.Name}");
    }
}