namespace P2P.Node.Models;

internal class AppTopology
{
    public AppNode? PreviousNode { get; set; }
    public AppNode? NextNode { get; set; }
    public AppNode? NextNextNode { get; set; }
    public AppNode? Leader { get; set; }
}