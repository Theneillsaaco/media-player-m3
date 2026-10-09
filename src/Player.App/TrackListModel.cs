using Qt.Bridge.Models;

namespace Player.App;

public class TrackListModel : ListModel<TrackRow>
{
    private List<TrackRow> _rows = new();

    public override int ItemCount() => _rows.Count;

    public override TrackRow Data(int index) => index >= 0 && index < _rows.Count ? _rows[index] : null!;

    [Qt.Ignore]
    public void Replace(IEnumerable<TrackRow> rows)
    {
        BeginResetModel();
        try { _rows = rows.ToList(); }
        finally { EndResetModel(); }
    }
}