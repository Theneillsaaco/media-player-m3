namespace Player.App.Services;

/// <summary>The one-line message under the search box ("Searching…", "Playing", errors). Written on the Qt thread only.</summary>
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
    } = "Search for a song or paste a link or path";
}