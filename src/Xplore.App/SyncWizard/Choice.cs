using Xplore.Core.Localization;

namespace Xplore.App;

public sealed record Choice<T>(T Value, string Label) where T : struct, Enum
{
    public static IReadOnlyList<Choice<T>> All { get; } =
        [.. Enum.GetValues<T>().Select(v => new Choice<T>(v, Loc.Label(v)))];
}
