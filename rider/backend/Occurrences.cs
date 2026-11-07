using JetBrains.Application.UI.PopupLayout;
using JetBrains.IDE;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.Occurrences;
using JetBrains.ReSharper.Psi.Search;

namespace AnotherFSM.Rider;

[OccurrenceProvider]
public sealed class FsmOccurrenceProvider : IOccurrenceProvider
{
    public IOccurrence MakeOccurrence(FindResult result) => result is FsmKeyResult key ? new FsmOccurrence(key) : null!;
}

internal sealed class FsmOccurrence(FsmKeyResult result) : RangeOccurrence(result.File, result.Range, false), IOccurrence
{
    public bool Expired => !result.Current;
    public long Generation => result.Generation;
    bool IOccurrence.IsValid => !Expired && base.IsValid;
    public override string GetDisplayNameText() =>
        (Expired ? "AnotherFSM — 结果已过期，请刷新 Find Usages：" : "AnotherFSM — ")
        + (result.Reference.ImplicitKey ? "隐式类型键 " : "工厂类型键 ") + result.Reference.TypeKey
        + " · " + result.BindingReason + " · 宿主 " + string.Join("; ", result.Hosts);
    public override bool Navigate(ISolution solution, PopupWindowContextSource windowContext, bool transferFocus, TabOptions tabOptions)
        => !Expired && base.Navigate(solution, windowContext, transferFocus, tabOptions);
}
