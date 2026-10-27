namespace Xplore.Core.Settings;

// Shows paths the way the settings ask for: with "/" instead of "\" when ForwardSlashes is on.
public sealed class PathFormat(ISettings settings)
{
    public string Show(string path) => settings.Current.ForwardSlashes ? path.Replace('\\', '/') : path;
}
