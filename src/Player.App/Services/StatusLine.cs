namespace Player.App.Services;

/// <summary>The one-line message under the search box ("Buscando…", "Reproduciendo", errors). Written on the Qt thread only.</summary>
internal sealed class StatusLine
{
    public event Action? Changed;

    /// <summary>The last message; the search hint while there is none.</summary>
    public string Text
    {
        get => field.Length > 0 ? field : Tr.T("search.hint");
        set
        {
            if (field == value)
                return;

            field = value;
            Changed?.Invoke();
        }
    } = "";

    /// <summary>The language changed: whoever shows the text reads it again.</summary>
    public void Refresh() => Changed?.Invoke();
}
