using Xplore.Sync;

namespace Xplore.App;

public sealed record PlanRow(int Number, PlanStep Step)
{
    public PlanCommand Command => Step.Command;
    public string CommandName => Step.CommandName;
    public string Path => Step.Path;
    public string Comment => Step.Comment ?? "";
}
