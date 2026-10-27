using Xplore.Core.Localization;
using Xplore.Core.History;
using Xplore.Core.Settings;

namespace Xplore.App;

public sealed class HistoryNodeViewModel(HistoryNode node, PathFormat format, bool isCurrent, IReadOnlyList<HistoryNodeViewModel> children)
{
    public HistoryNode Node { get; } = node;
    public string Label => Node.Action?.Name ?? Loc.Get("History.Initial");
    public string Tooltip => Node.Action?.Describe(false, format) ?? Loc.Get("History.Initial.Tooltip");
    public string Details => Node.Action?.Describe(true, format) ?? Loc.Get("History.Initial.Tooltip");
    public bool IsGreyed => Node.Greyed;
    public bool IsCurrent { get; } = isCurrent;
    public IReadOnlyList<HistoryNodeViewModel> Children { get; } = children;
}
