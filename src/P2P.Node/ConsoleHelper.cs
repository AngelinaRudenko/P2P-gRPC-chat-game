using P2P.Node.Models;

namespace P2P.Node;

internal static class ConsoleHelper
{
    internal static void WriteGreen(string message)
    {
        Console.ForegroundColor = ConsoleColor.DarkGreen;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    internal static void WriteRed(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    internal static void Debug(string message)
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    internal static void LogTopology(AppTopology topology)
    {
        WriteGreen($"Previous {topology.PreviousNode?.Name}, next {topology.NextNode?.Name}," + 
                   $" next next {topology.NextNextNode?.Name}, leader {topology.Leader?.Name}");
    }
}