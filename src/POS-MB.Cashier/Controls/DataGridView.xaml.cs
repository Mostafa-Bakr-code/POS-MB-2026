namespace POS_MB.Cashier.Controls;

// Replaces WinForms' DataGridView, used by ~6 screens being ported
// (Categories, Items, Users, OrderHistory, Logs, Reports). Columns are
// defined at runtime (a plain list, not a fixed XAML DataTemplate) since
// Reports needs a genuinely different column set per report type - that's
// exactly what SetColumns supports: call it again with a new column list
// and both the header and the row template rebuild to match.
//
// Row-tap and row actions are plain C# events/callbacks, not bindable
// commands - consistent with this project's no-MVVM, code-behind-only
// style (see POS-MB.Mobile).
public partial class DataGridView : ContentView
{
    private IReadOnlyList<GridColumn> _columns = [];
    private IReadOnlyList<GridRowAction> _rowActions = [];

    public event EventHandler<object>? RowTapped;

    public DataGridView()
    {
        InitializeComponent();
    }

    public void SetColumns(IReadOnlyList<GridColumn> columns, IReadOnlyList<GridRowAction>? rowActions = null)
    {
        _columns = columns;
        _rowActions = rowActions ?? [];

        BuildHeader();
        ItemsView.ItemTemplate = new DataTemplate(BuildRow);
    }

    public IEnumerable<object>? ItemsSource
    {
        get => ItemsView.ItemsSource as IEnumerable<object>;
        set => ItemsView.ItemsSource = value;
    }

    private void BuildHeader()
    {
        HeaderGrid.ColumnDefinitions.Clear();
        HeaderGrid.Children.Clear();

        foreach (var column in AllColumnWeights())
            HeaderGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(column, GridUnitType.Star)));

        for (var i = 0; i < _columns.Count; i++)
        {
            var label = new Label
            {
                Text = _columns[i].Header,
                TextColor = Colors.White,
                FontAttributes = FontAttributes.Bold,
                LineBreakMode = LineBreakMode.TailTruncation
            };
            Grid.SetColumn(label, i);
            HeaderGrid.Children.Add(label);
        }

        // Row actions get a blank header cell each - the buttons themselves
        // (Edit/Deactivate/etc.) already say what they do.
    }

    private View BuildRow()
    {
        var grid = new Grid { Padding = new Thickness(8, 10) };

        foreach (var weight in AllColumnWeights())
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(weight, GridUnitType.Star)));

        for (var i = 0; i < _columns.Count; i++)
        {
            var column = _columns[i];
            var label = new Label { LineBreakMode = LineBreakMode.TailTruncation, VerticalOptions = LayoutOptions.Center };
            label.SetBinding(Label.TextProperty, new Binding(".", converter: new FuncConverter(column.GetText)));
            Grid.SetColumn(label, i);
            grid.Children.Add(label);
        }

        for (var i = 0; i < _rowActions.Count; i++)
        {
            var action = _rowActions[i];
            var button = new Button { Text = action.Label, FontSize = 12, Padding = new Thickness(6, 2) };
            button.Clicked += (_, _) =>
            {
                if (grid.BindingContext is not null) action.OnClick(grid.BindingContext);
            };
            Grid.SetColumn(button, _columns.Count + i);
            grid.Children.Add(button);
        }

        var tapGesture = new TapGestureRecognizer();
        tapGesture.Tapped += (_, _) =>
        {
            if (grid.BindingContext is not null) RowTapped?.Invoke(this, grid.BindingContext);
        };
        grid.GestureRecognizers.Add(tapGesture);

        return grid;
    }

    // Data columns use each GridColumn's own weight; every row-action button
    // gets a small fixed weight of its own, appended after them.
    private IEnumerable<double> AllColumnWeights() =>
        _columns.Select(c => c.Weight).Concat(_rowActions.Select(_ => 0.6));
}
