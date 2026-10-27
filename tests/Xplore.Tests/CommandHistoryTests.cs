using Xplore.Core.Shell;

namespace Xplore.Tests;

public class CommandHistoryTests
{
    private static CommandHistory With(params string[] commands)
    {
        var history = new CommandHistory();
        foreach (var command in commands)
        {
            history.Add(command);
        }

        return history;
    }

    [Fact]
    public void EmptyHistoryRecallsNothing()
    {
        var history = new CommandHistory();

        Assert.Null(history.Previous());
        Assert.Null(history.Next());
    }

    [Fact]
    public void PreviousWalksBackAndStopsAtTheOldest()
    {
        var history = With("a", "b", "c");

        Assert.Equal(["c", "b", "a", "a"], [history.Previous(), history.Previous(), history.Previous(), history.Previous()]);
    }

    [Fact]
    public void NextWalksForwardToAnEmptyLine()
    {
        var history = With("a", "b");
        history.Previous();
        history.Previous();

        Assert.Equal(["b", "", ""], [history.Next(), history.Next(), history.Next()]);
    }

    [Fact]
    public void AddingResetsTheCursorToTheNewest()
    {
        var history = With("a", "b");
        history.Previous();
        history.Previous();
        history.Add("c");

        Assert.Equal("c", history.Previous());
    }
}
