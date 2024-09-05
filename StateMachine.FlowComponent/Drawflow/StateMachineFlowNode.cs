namespace StateMachine;

public class StateMachineFlowNode<TData>
{
    public string? Class { get; set; }

    public string? Html { get; set; }

    public string? Id { get; set; }

    public string? Name { get; set; }

    public double Pos_X { get; set; }

    public double Pos_Y { get; set; }

    public double Offset_X { get; set; }

    public double Offset_Y { get; set; }

    public TData? Data { get; set; }
}
