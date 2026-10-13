using Qt.Bridge.Models;

namespace Player.App.Models;

/// <summary>One line in the lyrics panel. Start is in seconds, -1 for plain lyrics.</summary>
public class LyricRow
{
    public string Text { get; set; } = "";

    public double Start { get; set; } = -1;
}

public class LyricsListModel : ListModel<LyricRow>
{
    private List<LyricRow> _rows = new();

    public override int ItemCount() => _rows.Count;

    public override LyricRow Data(int index) => index >= 0 && index < _rows.Count ? _rows[index] : null!;

    [Qt.Ignore]
    public void Replace(IEnumerable<LyricRow> rows)
    {
        BeginResetModel();
        try { _rows = rows.ToList(); }
        finally { EndResetModel(); }
    }
}
