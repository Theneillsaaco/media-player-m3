using Player.App.ViewModels.Base;
using Player.Core.Localization;

namespace Player.App.ViewModels;

/// <summary>
/// The interface language for QML. Instantiated once from Main.qml as <c>LocaleViewModel { id: localeVm }</c>; the
/// Tr component there turns <see cref="StringsJson"/> into texts. Choose, not Set: the bridge would read SetX as a setter.
/// </summary>
public class LocaleViewModel : ViewModelBase
{
    public LocaleViewModel()
    {
        AppServices.EnsureStarted();
        Sync();
    }

    /// <summary>"es" or "en".</summary>
    public string Language { get; private set => Set(ref field, value); } = "en";

    /// <summary>A JSON object with every text of the current language.</summary>
    public string StringsJson { get; private set => Set(ref field, value); } = "{}";

    public void ChooseLanguage(string language)
    {
        AppServices.ChooseLanguage(language);
        Sync();
    }

    private void Sync()
    {
        Language = I18n.current();
        StringsJson = I18n.tableJson(Language);
    }
}
