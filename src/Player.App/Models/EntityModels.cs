using Qt.Bridge.Models;

namespace Player.App.Models;

/// <summary>
/// A row of a browse list. Kind is "album", "artist", "playlist" or "track" (a song in a shelf of Home), or
/// "header" (a shelf title, only Title is set). EntityId is the browse id, or the video id for a track.
/// </summary>
public class EntityRow
{
    public string Kind { get; set; } = "";

    public string EntityId { get; set; } = "";

    public string Title { get; set; } = "";

    public string Subtitle { get; set; } = "";

    public string ArtUrl { get; set; } = "";
}

public class EntityListModel : ListModel<EntityRow>
{
    private List<EntityRow> _rows = new();

    private static readonly EntityRow Empty = new();

    public override int ItemCount() => _rows.Count;

    public override EntityRow Data(int index) => index >= 0 && index < _rows.Count ? _rows[index] : Empty;

    [Qt.Ignore]
    public void Replace(IEnumerable<EntityRow> rows)
    {
        BeginResetModel();
        try { _rows = rows.ToList(); }
        finally { EndResetModel(); }
    }
}
