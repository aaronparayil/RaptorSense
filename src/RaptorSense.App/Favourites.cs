namespace RaptorSense.App;

/// <summary>The effects starred in the gallery, kept by id in appearance.json.</summary>
static class Favourites
{
    const string Key = "FavouriteEffects";

    static readonly List<string> ids = Load();

    /// <summary>Raised on the UI thread when an effect is starred or unstarred.</summary>
    public static event Action? Changed;

    public static IReadOnlyList<string> Ids => ids;

    public static bool Contains(string id) => ids.Contains(id, StringComparer.OrdinalIgnoreCase);

    public static void Set(string id, bool favourite)
    {
        if (favourite == Contains(id)) return;
        if (favourite) ids.Add(id);
        else ids.RemoveAll(i => i.Equals(id, StringComparison.OrdinalIgnoreCase));
        AppearanceSettings.Set(Key, ids.Count == 0 ? null : string.Join(",", ids));
        Changed?.Invoke();
    }

    static List<string> Load() =>
        (AppearanceSettings.Get(Key) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
