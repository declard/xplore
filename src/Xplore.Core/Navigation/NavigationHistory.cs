using System.Collections.Immutable;

namespace Xplore.Core.Navigation;

// Browser-style history; Current is the folder path, null meaning the virtual "This PC" root.
public sealed record NavigationHistory(
    string? Current,
    ImmutableStack<string?> BackStack,
    ImmutableStack<string?> ForwardStack)
{
    public static NavigationHistory Start(string? path = null) =>
        new(path, ImmutableStack<string?>.Empty, ImmutableStack<string?>.Empty);

    public bool CanGoBack => !BackStack.IsEmpty;
    public bool CanGoForward => !ForwardStack.IsEmpty;
    public bool CanGoUp => Current is not null;

    public NavigationHistory Navigate(string? path) =>
        path == Current ? this : new(path, BackStack.Push(Current), ImmutableStack<string?>.Empty);

    public NavigationHistory Back() =>
        CanGoBack ? new(BackStack.Peek(), BackStack.Pop(), ForwardStack.Push(Current)) : this;

    public NavigationHistory Forward() =>
        CanGoForward ? new(ForwardStack.Peek(), BackStack.Push(Current), ForwardStack.Pop()) : this;

    public NavigationHistory BackBy(int steps) => Enumerable.Range(0, steps).Aggregate(this, (h, _) => h.Back());

    public NavigationHistory ForwardBy(int steps) => Enumerable.Range(0, steps).Aggregate(this, (h, _) => h.Forward());

    public NavigationHistory Up() => CanGoUp ? Navigate(Path.GetDirectoryName(Current)) : this;
}
