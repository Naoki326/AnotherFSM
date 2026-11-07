using FixtureSafety;
using NodeTag = StateMachine.FSMNodeAttribute;
using NodeContract = StateMachine.IFSMNode;
namespace Issue34.ProjectLibrary;

[NodeTag("ProjectLookupKey")]
public sealed class DependencyWorker : ControlledNode { }

public static class Warehouse
{
    [NodeTag("NestedLookupKey")]
    public sealed class ShelfWorker : ControlledNode { }
    // Implementing IFSMNode alone does not register a factory key.
    public sealed class UnregisteredShelfWorker : ControlledNode { }
}

// Open generic definitions are not concrete factory registrations.
[NodeTag("OpenGenericKey")]
public class BufferWorker<T> : ControlledNode { }

// A concrete, non-generic class with a constructed generic base IS a positive case.
[NodeTag("ClosedBufferKey")]
public sealed class TextBufferWorker : BufferWorker<string>, NodeContract { }

public class PlainBuffer<T> { }
[NodeTag("ClosedNonNodeKey")]
public sealed class TextRecord : PlainBuffer<string> { }
[NodeTag("AbstractClosedKey")]
public abstract class AbstractIntBufferWorker : BufferWorker<int> { }

public class GenericWarehouse<T>
{
    [NodeTag("OpenOuterKey")]
    public sealed class SlotWorker : ControlledNode { }
}

[NodeTag("PlainNotNodeKey")]
public sealed class OrdinaryRecord { }
[NodeTag("AbstractOnlyKey")]
public abstract class AbstractWorker : ControlledNode { }
[NodeTag("IgnoredCtorKey", Key = "NamedOverrideKey")]
public sealed class NamedWorker : ControlledNode { }
[NodeTag("ForbiddenFallbackKey", Key = null!)]
public sealed class NullKeyWorker : ControlledNode { }
