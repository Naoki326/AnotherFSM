namespace StateMachine
{
    public class FlowConnectionArgs
    {
        public string OutputId { get; set; } = default!;

        public string InputId { get; set; } = default!;

        public string OutputClass { get; set; } = default!;

        public string InputClass { get; set; } = default!;
    }

    public class FlowConnectionError
    {
        public int ErrorId { get; set; } = default!;

        public string ErrorMessage { get; set; } = default!;
    }
}
