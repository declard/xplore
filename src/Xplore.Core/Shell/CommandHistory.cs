namespace Xplore.Core.Shell;

// The commands a user submitted in a console and a cursor for recalling them with the arrow keys.
public sealed class CommandHistory
{
    private readonly List<string> _commands = [];
    private int _index;

    public void Add(string command)
    {
        _commands.Add(command);
        _index = _commands.Count;
    }

    // Null when there is nothing to recall; an empty string once the cursor moves past the newest command.
    public string? Previous() => Move(-1);

    public string? Next() => Move(1);

    private string? Move(int step)
    {
        if (_commands is [])
            return null;

        _index = Math.Clamp(_index + step, 0, _commands.Count);
        return _index == _commands.Count ? "" : _commands[_index];
    }
}
