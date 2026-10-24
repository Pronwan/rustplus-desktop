namespace RustPlusDesk.Helpers;

/// <summary>
/// Looks a string up in the language the user chose, with a fallback for keys that do not exist
/// yet.
///
/// The obvious spelling — <c>Resources.ResourceManager.GetString(key)</c> — quietly ignores the
/// app's language setting: passed no culture, ResourceManager falls back to the thread's UI
/// culture, which is the operating system's language. On a German Windows with the app set to
/// English, those strings came back German.
///
/// <see cref="Properties.Resources.GetString"/> passes the configured culture and falls back to
/// the neutral resources, so everything routes through it. It returns the key itself when nothing
/// is found, which is what the fallback here replaces.
/// </summary>
public static class Loc
{
    public static string Text(string key, string fallback)
        => TextOrNull(key) ?? fallback;

    /// <summary>Null rather than the key when a string is missing, for callers with their own default.</summary>
    public static string? TextOrNull(string key)
    {
        var value = Properties.Resources.GetString(key);

        return string.IsNullOrWhiteSpace(value) || value == key ? null : value;
    }
}
