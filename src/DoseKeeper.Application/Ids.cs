namespace DoseKeeper.Application;

internal static class Ids
{
    /// <summary>Short, prefixed, URL-safe ids (e.g. "med-3f9c2a1b7d"); never long digit runs.</summary>
    public static string New(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 11)];
}
