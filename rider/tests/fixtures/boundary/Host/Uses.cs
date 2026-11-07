using FixtureSafety;
namespace Issue34.Boundary;
// Real C# use sites keep Find Usages a non-singleton native result, not automatic navigation.
// Expected temporary compiler errors after removing a project item/reference are intentional.
public static class Uses
{
    public static Type[] Types
    {
        get
        {
            Execution.Forbidden<object>("Boundary.Uses.Types.get");
            return new[] { typeof(ReferenceWorker), typeof(ProjectWorker),
#if NET8_0
                typeof(LegacyWorker)
#else
                typeof(ModernWorker)
#endif
            };
        }
    }
}
