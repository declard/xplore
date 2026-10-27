using System.Windows.Markup;
using Xplore.Core.Localization;

namespace Xplore.App.Localization;

// {l:Loc Key}: the text of a resource key, resolved when the XAML is loaded.
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.Get(key);
}
