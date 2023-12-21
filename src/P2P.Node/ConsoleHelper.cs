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
        Console.ForegroundColor = ConsoleColor.DarkRed;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    internal static void Debug(string message)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    internal static void LogTopology(Proto.Topology topology)
    {
        WriteGreen($"Previous {topology.PreviousNode?.Name}, next {topology.NextNode?.Name}," +
                   $" next next {topology.NextNextNode?.Name}, leader {topology.Leader?.Name}");
    }
}