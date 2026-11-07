using System;
using FixtureSafety;
using StateMachine;

namespace Issue34.SourceMatrix;

// Same physical declaration, independently compiled into each Matrix host assembly/TFM.
[FSMNode("MatrixCurrentKey")]
public sealed class CurrentWorker : ControlledNode
{
    public CurrentWorker() => Execution.Forbidden<object>("SourceMatrix.CurrentWorker.ctor");
    public override void Execute() => Execution.Forbidden<object>("SourceMatrix.CurrentWorker.Execute");
}

// Two genuine C# type-reference positions per origin, even after deduplicating physical source ranges.
// Native Find Usages may start at either witness. No getter is invoked during preparation/analysis.
public static class SourceWitness
{
    public static Type[] Types
    {
        get
        {
            Execution.Forbidden<object>("SourceMatrix.SourceWitness.Types.get");
            return new[] {
                typeof(CurrentWorker),
                typeof(Issue34.MatrixDependency.ProjectWorker),
                typeof(Issue34.BinaryLibrary.BinaryWorker),
                typeof(Issue34.PackageLibrary.ParcelWorker) };
        }
    }

    public static Type[] MoreTypes
    {
        get
        {
            Execution.Forbidden<object>("SourceMatrix.SourceWitness.MoreTypes.get");
            return new[] {
                typeof(CurrentWorker),
                typeof(Issue34.MatrixDependency.ProjectWorker),
                typeof(Issue34.BinaryLibrary.BinaryWorker),
                typeof(Issue34.PackageLibrary.ParcelWorker) };
        }
    }
}
