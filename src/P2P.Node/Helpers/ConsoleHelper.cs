namespace P2P.Node.Helpers;

internal class ConsoleHelper
{
    internal static string? ReadFromConsoleUntilPredicate(string message, Predicate<string?> inputRepeatPredicate)
    {
        string? input = null;
        while (inputRepeatPredicate(input))
        {
            Console.WriteLine(message);
            input = Console.ReadLine();
        }
        return input;
    }
}