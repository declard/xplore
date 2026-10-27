using YamlDotNet.Serialization;

namespace Xplore.Core.Settings;

public static class Yaml
{
    public static ISerializer Writer { get; } = new SerializerBuilder()
        .DisableAliases()
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    public static IDeserializer Reader { get; } = new DeserializerBuilder()
        .WithTypeMapping<IReadOnlyList<Bookmark>, List<Bookmark>>()
        .WithTypeMapping<IReadOnlyList<OpenWithApp>, List<OpenWithApp>>()
        .WithTypeMapping<IReadOnlyDictionary<string, IReadOnlyList<OpenWithApp>>, Dictionary<string, IReadOnlyList<OpenWithApp>>>()
        .IgnoreUnmatchedProperties()
        .Build();
}
