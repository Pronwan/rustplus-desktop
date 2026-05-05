namespace RustPlusDesk.Localization;

/// <summary>
/// Static convenience facade for code-behind. Equivalent to
/// LocalizationManager.Instance.T(...). Use Loc.T("key") in C# files;
/// use {l:Loc Key=...} in XAML.
/// </summary>
public static class Loc
{
    public static string T(string key, params object[] args)
        => LocalizationManager.Instance.T(key, args);
}
