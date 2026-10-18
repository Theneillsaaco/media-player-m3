using Player.Core.Localization;

namespace Player.App.Services;

/// <summary>Texts of the interface in the current language (see Translations.fs).</summary>
internal static class Tr
{
    public static string T(string key) => I18n.t(key);

    public static string F(string key, params object[] args) => I18n.tf(key, args);
}
