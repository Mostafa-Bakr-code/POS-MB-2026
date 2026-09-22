using POS_MB.Cashier.Controls;
using POS_MB.Cashier.Models;
using POS_MB.Cashier.Session;

namespace POS_MB.Cashier.Pages;

// Replaces FormItemPriceHistoryDialog - read-only, pushed as a normal
// (non-modal) page since there's nothing to complete/cancel.
public partial class ItemPriceHistoryPage : ContentPage
{
    public ItemPriceHistoryPage(string itemName, List<ItemPriceHistoryDto> history)
    {
        InitializeComponent();
        Title = $"Price History - {itemName}";

        HistoryGrid.SetColumns(
        [
            new GridColumn("Date", h => AppSession.ToLocalDisplay(((ItemPriceHistoryDto)h).ChangedAt).ToString("yyyy-MM-dd HH:mm"), weight: 1.2),
            new GridColumn("Changed By", h => ((ItemPriceHistoryDto)h).ChangedByUserName ?? "(unknown)", weight: 1),
            new GridColumn("Price", h => $"{((ItemPriceHistoryDto)h).OldPrice:0.00} -> {((ItemPriceHistoryDto)h).NewPrice:0.00}", weight: 1.2),
            new GridColumn("Tax Rate", h => $"{((ItemPriceHistoryDto)h).OldTaxRate:0.##}% -> {((ItemPriceHistoryDto)h).NewTaxRate:0.##}%", weight: 1.2)
        ]);

        HistoryGrid.ItemsSource = history.Cast<object>();
    }

    private async void OnCloseClicked(object? sender, EventArgs e) => await Navigation.PopAsync();
}
