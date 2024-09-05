namespace StateMachine
{
    public class NodeData
    {
        public string Color { get; set; } = default!;
        public string Message { get; set; } = default!;
        public string Type { get; set; } = default!;
        public string Name { get; set; } = default!;
    }

    public class ConnectionData
    {
        public string FromNodeName { get; set; } = default!;

        public string ToNodeName { get; set; } = default!;
    }

}
