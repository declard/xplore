namespace Xplore.Core.Navigation;

// Type-ahead prefix: a character extends the prefix when typed within the timeout of the previous one, otherwise it starts a new prefix.
public sealed class PrefixSearch
{
    private DateTime _last = DateTime.MinValue;

    public string Prefix { get; private set; } = "";

    public string Add(char c, DateTime now, TimeSpan timeout)
    {
        Prefix = now - _last <= timeout ? Prefix + c : c.ToString();
        _last = now;
        return Prefix;
    }

    // First match at or after `from`, wrapping around the end of the list; -1 when there is none.
    public static int IndexOf(IReadOnlyList<string> names, string prefix, int from = 0) =>
        Enumerable.Range(0, names.Count)
            .Select(k => (from + k) % names.Count)
            .FirstOrDefault(i => names[i].StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase), -1);
}
