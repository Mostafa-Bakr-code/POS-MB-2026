namespace POS_MB.Cashier.Controls;

// One column's definition for DataGridView - Weight follows MAUI's own
// star-sizing convention (a column with Weight=2 gets twice the width of a
// Weight=1 column), not a fixed pixel width, so the grid stays responsive
// across phone/tablet/Windows window sizes.
public class GridColumn(string header, Func<object, string> getText, double weight = 1)
{
    public string Header { get; } = header;
    public Func<object, string> GetText { get; } = getText;
    public double Weight { get; } = weight;
}

// One row-level action button (e.g. "Edit", or a "Deactivate"/"Reactivate"
// toggle whose label depends on the row) - rendered after the data columns.
// OnClick receives the row object the button was on.
public class GridRowAction(Func<object, string> getLabel, Action<object> onClick)
{
    // Convenience overload for the common case (every row's button says
    // the same fixed thing, e.g. "Edit").
    public GridRowAction(string label, Action<object> onClick) : this(_ => label, onClick) { }

    public Func<object, string> GetLabel { get; } = getLabel;
    public Action<object> OnClick { get; } = onClick;
}
