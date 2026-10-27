using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Xplore.Core.History;

// A reversible user action. UndoAsync/RedoAsync fault with IOException when the file system no longer matches what the action expects.
public interface IHistoryAction
{
    string Name { get; }
    bool Undoable { get; }
    Task UndoAsync();
    Task RedoAsync();
    string Describe(bool extended);
}

// A node of the action history tree. Action: null for the hidden root; Children: ordered, the last one is the rightmost; Failed: replaying it threw.
public sealed class HistoryNode(IHistoryAction? action, HistoryNode? parent)
{
    public IHistoryAction? Action { get; } = action;
    public HistoryNode? Parent { get; set; } = parent;
    public List<HistoryNode> Children { get; } = [];
    public bool Failed { get; set; }

    public bool IsRoot => Action is null;
    public bool Greyed => Action is { Undoable: false } || Failed;
}

public sealed class ActionHistory
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ActionHistory(int maxDepth)
    {
        MaxDepth = Math.Max(1, maxDepth);
        Current = Root;
    }

    public event Action? Changed;

    public HistoryNode Root { get; } = new(null, null);
    public HistoryNode Current { get; private set; }
    public int MaxDepth { get; }

    public HistoryNode Record(IHistoryAction action)
    {
        var node = new HistoryNode(action, Current);
        Current.Children.Add(node);
        Current = node;
        Trim();
        Changed?.Invoke();
        return node;
    }

    // Replays are serialized so that overlapping undo/redo requests never interleave their walks over the tree.
    public Task<bool> UndoAsync() =>
        Serialized(async () =>
        {
            var done = false;
            for (var n = Current; !n.IsRoot && !done; n = n.Parent!)
            {
                Current = n.Parent!;
                done = await ReplayAsync(n, a => a.UndoAsync()) == true;
            }

            Changed?.Invoke();
            return done;
        });

    public Task<bool> RedoAsync() =>
        Serialized(async () =>
        {
            var done = false;
            while (!done && Current.Children is [.., var next])
            {
                Current = next;
                done = await ReplayAsync(next, a => a.RedoAsync()) == true;
            }

            Changed?.Invoke();
            return done;
        });

    // Undoes up to the common ancestor, redoes down to the target, then makes the path to the target the rightmost one.
    public Task<IReadOnlyList<HistoryNode>> ReplayToAsync(HistoryNode target) =>
        Serialized<IReadOnlyList<HistoryNode>>(async () =>
        {
            List<HistoryNode> failed = [];
            var down = Chain(target);
            var up = Chain(Current);
            var common = up.Intersect(down).First();

            foreach (var n in up.TakeWhile(n => n != common))
            {
                Current = n.Parent!;
                if (await ReplayAsync(n, a => a.UndoAsync()) == false)
                {
                    failed.Add(n);
                }
            }

            foreach (var n in down.TakeWhile(n => n != common).Reverse())
            {
                Current = n;
                if (await ReplayAsync(n, a => a.RedoAsync()) == false)
                {
                    failed.Add(n);
                }
            }

            Current = target;
            for (var n = target; !n.IsRoot; n = n.Parent!)
            {
                n.Parent!.Children.Remove(n);
                n.Parent.Children.Add(n);
            }

            Changed?.Invoke();
            return failed;
        });

    private static List<HistoryNode> Chain(HistoryNode node)
    {
        List<HistoryNode> chain = [];
        for (var n = node; n is not null; n = n.Parent)
        {
            chain.Add(n);
        }

        return chain;
    }

    // Greyed nodes are skipped (null); a throwing node is marked failed (false).
    private static async Task<bool?> ReplayAsync(HistoryNode node, Func<IHistoryAction, Task> step)
    {
        if (node.Greyed)
            return null;

        try
        {
            await step(node.Action!);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or Win32Exception)
        {
            node.Failed = true;
            return false;
        }
    }

    private async Task<T> Serialized<T>(Func<Task<T>> walk)
    {
        await _gate.WaitAsync();
        try
        {
            return await walk();
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Trim()
    {
        while (Chain(Current).Count - 1 > MaxDepth)
        {
            var top = Chain(Current)[^2];
            var index = Root.Children.IndexOf(top);
            Root.Children.RemoveAt(index);
            Root.Children.InsertRange(index, top.Children);
            top.Children.ForEach(c => c.Parent = Root);
        }
    }
}
