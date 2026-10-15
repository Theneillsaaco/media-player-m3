namespace Player.App.Services;

/// <summary>The one-line message under the search box ("Buscando…", "Reproduciendo", errors). Written on the Qt thread only.</summary>
internal sealed class StatusLine
{
    public event Action? Changed;

    public string Text
    {
        get;
        set
        {
            if (field == value)
                return;

            field = value;
            Changed?.Invoke();
        }
    } = "Busca una canción o pega un enlace o ruta";
}
