using Xplore.Core.Localization;
using Xplore.Sync;

namespace Xplore.App;

public sealed record ResultRow(StepResult Result)
{
    public int Number => Result.Index + 1;
    public StepOutcome Outcome => Result.Outcome;
    public string OutcomeText => Loc.Label(Result.Outcome);
    public string CommandName => Result.Step.CommandName;
    public string Path => Result.Step.Path;
    public string Message => Result.Message ?? "";
}
