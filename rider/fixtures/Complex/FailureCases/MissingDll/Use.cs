// Expected compiler failure CS0246 (and unresolved namespace). The real producer defines this type.
using Issue34.BinaryLibrary;
namespace Issue34.Failures;
public sealed class MissingUse
{
    public MissingUse() => throw new InvalidOperationException("ISSUE34_BUSINESS_EXECUTION: MissingUse.ctor");
    public BinaryWorker Worker => throw new InvalidOperationException("ISSUE34_BUSINESS_EXECUTION: MissingUse.Worker.get");
}
