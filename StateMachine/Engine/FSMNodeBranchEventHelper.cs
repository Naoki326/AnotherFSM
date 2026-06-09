namespace StateMachine;

public static class FSMNodeBranchEventHelper
{
    public static bool PublishesEvent(IFSMNode node, string eventName)
    {
        node.UpdateEventDescriptions();
        return (node.EventDescriptions ?? []).Any(desc =>
            string.Equals(desc.Description, eventName, StringComparison.OrdinalIgnoreCase));
    }

    public static bool EnsurePublishesEvent(IFSMNode node, FSMEvent fsmEvent)
    {
        node.UpdateEventDescriptions();
        var descriptions = node.EventDescriptions ?? [];

        if (PublishesEvent(node, fsmEvent.EventName))
        {
            return true;
        }

        if (descriptions.Count == 1)
        {
            node.SetBranchEvent(descriptions[0].Index, fsmEvent);
            node.UpdateEventDescriptions();
            return true;
        }

        if (descriptions.Count == 0)
        {
            node.SetBranchEvent((int)FSMEnum.Next, fsmEvent);
            node.UpdateEventDescriptions();
            return true;
        }

        return false;
    }
}
