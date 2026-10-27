using Xplore.Core.Model;

namespace Xplore.Core.Locations;

// What a verb acts on. Item: the selected entries; Location: the location as a whole; Delete: like Item, and also what the Delete key does.
public enum VerbRole
{
    Item,
    Location,
    Delete,
}

// An action a provider adds to its location. Confirm: the question asked before running (null: none); Run: the action on the entries (empty for Location).
public sealed record LocationVerb(
    string Name,
    VerbRole Role,
    Func<IReadOnlyList<FsEntry>, string>? Confirm,
    Func<IReadOnlyList<FsEntry>, Task> Run);
