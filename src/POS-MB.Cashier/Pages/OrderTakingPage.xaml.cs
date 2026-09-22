using POS_MB.Cashier.Api;
using POS_MB.Cashier.Controls;
using POS_MB.Cashier.Models;
using POS_MB.Cashier.Session;
using POS_MB.Printing;

namespace POS_MB.Cashier.Pages;

// Replaces OrderTakingControl - the core cashier flow, and deliberately the
// most complex page in this port. Category/item tiles are built by hand
// into a FlexLayout (Wrap), same reasoning WinForms' FlowLayoutPanel tiles
// had - no CollectionView grid needed for a small, rarely-changing set of
// buttons. The cart, which DOES change constantly, uses a real
// CollectionView with a hand-built row template (same technique as
// DataGridView.BuildRow), since MAUI has no drag-free equivalent otherwise.
public partial class OrderTakingPage : ContentPage
{
    private readonly ApiClient _apiClient = new();

    private List<CategoryDto> _categories = [];
    private readonly List<CartLine> _cart = [];
    private int? _selectedCategoryId;

    public OrderTakingPage()
    {
        InitializeComponent();

        var canMarkComplimentary = AppSession.HasPermission(Permission.Complimentary);
        ComplimentaryCheck.IsEnabled = canMarkComplimentary;
        ComplimentaryLabel.Text = canMarkComplimentary
            ? "Complimentary Order (no charge)"
            : "Complimentary Order (no permission)";

        CartView.ItemTemplate = new DataTemplate(BuildCartRow);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadCategoriesAsync();
    }

    private async Task LoadCategoriesAsync()
    {
        CategoriesLayout.Children.Clear();
        _categories = await _apiClient.GetCategoriesAsync();

        foreach (var category in _categories)
        {
            var button = CreateTileButton(category.CategoryName, null);
            button.Clicked += async (s, _) =>
            {
                _selectedCategoryId = category.CategoryId;
                HighlightSelectedCategory((Button)s!);
                await LoadItemsAsync(category.CategoryId);
            };
            CategoriesLayout.Children.Add(button);
        }

        if (_categories.Count > 0 && CategoriesLayout.Children.Count > 0)
        {
            _selectedCategoryId = _categories[0].CategoryId;
            HighlightSelectedCategory((Button)CategoriesLayout.Children[0]);
            await LoadItemsAsync(_categories[0].CategoryId);
        }
    }

    private void HighlightSelectedCategory(Button selected)
    {
        foreach (var child in CategoriesLayout.Children)
        {
            if (child is not Button button) continue;
            button.BackgroundColor = button == selected ? Color.FromArgb("#0D6EFD") : Color.FromArgb("#E9ECEF");
            button.TextColor = button == selected ? Colors.White : Colors.Black;
        }
    }

    private async Task LoadItemsAsync(int categoryId)
    {
        ItemsLayout.Children.Clear();
        var items = await _apiClient.GetItemsAsync(categoryId, availableOnly: true);

        foreach (var item in items)
        {
            var button = CreateTileButton(item.ItemName, item.Price.ToString("0.00"));
            button.Clicked += (_, _) => AddToCart(item, (int)QuantityStepper.Value);
            ItemsLayout.Children.Add(button);
        }
    }

    private static Button CreateTileButton(string title, string? subtitle) => new()
    {
        Text = subtitle is null ? title : $"{title}\n{subtitle}",
        WidthRequest = 150,
        HeightRequest = 90,
        Margin = new Thickness(4),
        BackgroundColor = Color.FromArgb("#E9ECEF"),
        TextColor = Colors.Black,
        FontAttributes = FontAttributes.Bold,
        LineBreakMode = LineBreakMode.WordWrap
    };

    // Tapping a tile (quantity == 1, the common case) always adds its own
    // separate line so it can carry its own comment. Typing a bulk quantity
    // first instead adds ONE line with that quantity and one shared comment.
    private void AddToCart(ItemDto item, int quantity = 1)
    {
        // Groups with any existing line(s) for the same item instead of
        // always appending at the bottom - same reasoning as the cart's own
        // "+1" button (AddAnother).
        var lastIndex = _cart.FindLastIndex(c => c.Item.ItemId == item.ItemId);
        var newLine = new CartLine(item, quantity);

        if (lastIndex >= 0) _cart.Insert(lastIndex + 1, newLine);
        else _cart.Add(newLine);

        if (QuantityStepper.Value != 1) QuantityStepper.Value = 1;

        RenderCart();
    }

    private void AddAnother(CartLine existing)
    {
        var index = _cart.IndexOf(existing);
        _cart.Insert(index + 1, new CartLine(existing.Item));
        RenderCart();
    }

    private void RenderCart()
    {
        CartView.ItemsSource = null;
        CartView.ItemsSource = _cart;

        var total = _cart.Sum(c => c.Item.Price * c.Quantity);
        TotalLabel.Text = $"Total: {total:0.00}";
        PlaceOrderButton.IsEnabled = _cart.Count > 0;
    }

    private View BuildCartRow()
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(new GridLength(44)),
                new ColumnDefinition(new GridLength(44))
            },
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) },
            Padding = new Thickness(4),
            RowSpacing = 2
        };

        var nameLabel = new Label { FontAttributes = FontAttributes.Bold };
        Grid.SetColumn(nameLabel, 0);
        grid.Children.Add(nameLabel);

        var addButton = new Button { Text = "+", WidthRequest = 40, HeightRequest = 40, Padding = 0 };
        Grid.SetColumn(addButton, 1);
        grid.Children.Add(addButton);

        var removeButton = new Button { Text = "X", WidthRequest = 40, HeightRequest = 40, Padding = 0, TextColor = Colors.Red };
        Grid.SetColumn(removeButton, 2);
        grid.Children.Add(removeButton);

        var commentButton = new Button { FontSize = 12, HorizontalOptions = LayoutOptions.Start };
        Grid.SetRow(commentButton, 1);
        Grid.SetColumn(commentButton, 0);
        Grid.SetColumnSpan(commentButton, 3);
        grid.Children.Add(commentButton);

        grid.BindingContextChanged += (_, _) =>
        {
            if (grid.BindingContext is not CartLine line) return;

            nameLabel.Text = line.Quantity > 1
                ? $"{line.Item.ItemName} x{line.Quantity}  {(line.Item.Price * line.Quantity):0.00}"
                : $"{line.Item.ItemName}  {line.Item.Price:0.00}";

            commentButton.Text = string.IsNullOrWhiteSpace(line.Comment)
                ? "Add Comment"
                : $"Comment: {line.Comment}";
        };

        addButton.Clicked += (_, _) =>
        {
            if (grid.BindingContext is CartLine line) AddAnother(line);
        };
        removeButton.Clicked += (_, _) =>
        {
            if (grid.BindingContext is CartLine line) { _cart.Remove(line); RenderCart(); }
        };
        commentButton.Clicked += async (_, _) =>
        {
            if (grid.BindingContext is not CartLine line) return;

            var page = new TextInputPage($"Comment for {line.Item.ItemName}", "Comment (e.g. no onions)", line.Comment ?? "", maxLength: 50);
            await Navigation.PushModalAsync(page);
            if (!await page.Completion) return;

            line.Comment = page.Value.Length == 0 ? null : page.Value;
            RenderCart();
        };

        return grid;
    }

    private async void OnPlaceOrderClicked(object? sender, EventArgs e)
    {
        if (_cart.Count == 0) return;
        if (AppSession.CurrentUser is null)
        {
            await UiAlerts.Error(this, "No active user session.");
            return;
        }

        PlaceOrderButton.IsEnabled = false;
        try
        {
            if (await BothConfiguredPrintersUnreachableAsync())
            {
                await UiAlerts.Error(this,
                    "Cannot place this order: both the client and kitchen printers are unreachable. " +
                    "Check the printers and network connection, then try again.");
                return;
            }

            var request = new CreateOrderRequest(
                OrderSource.Cashier,
                AppSession.CurrentUser.UserId,
                IsComplimentary: ComplimentaryCheck.IsChecked,
                _cart.Select(c => new NewOrderItemRequest(c.Item.ItemId, c.Quantity, c.Comment)).ToList());

            var (order, error) = await _apiClient.CreateOrderAsync(request);
            if (order is null)
            {
                await UiAlerts.Error(this, $"Could not place the order: {error}");
                return;
            }

            // Built from order.Items (what the server actually confirmed and
            // stored), not from the local _cart - if a price changed between
            // adding to cart and placing the order, the receipt must match
            // what was really charged. order.Items has no item name, so
            // it's resolved from the cart's own items, whose names can't
            // have changed mid-order.
            var itemNamesById = _cart
                .Select(c => c.Item)
                .DistinctBy(i => i.ItemId)
                .ToDictionary(i => i.ItemId, i => i.ItemName);

            var receiptOrder = new ReceiptOrder(
                order.SerialNumber ?? order.OrderId,
                AppSession.ToLocalDisplay(order.Date),
                order.Items.Select(oi => new ReceiptItem(
                    itemNamesById.GetValueOrDefault(oi.ItemId, "Item"),
                    oi.Quantity, oi.Price, oi.TaxRate, oi.Comment)).ToList(),
                order.Total,
                order.IsComplimentary,
                order.OrderSource == OrderSource.Cashier ? "CASHIER" : "MOBILE");

            ShowStatus($"Order #{receiptOrder.SerialNumber} placed - Total {order.Total:0.00}", success: true);
            _cart.Clear();
            ComplimentaryCheck.IsChecked = false;
            RenderCart();

            // Fired in the background, not awaited here - the cashier moves
            // on to the next order immediately instead of waiting on two
            // printers.
            _ = PrintOrderAsync(receiptOrder);
        }
        finally
        {
            PlaceOrderButton.IsEnabled = true;
        }
    }

    // Checked before an order is even created - if a printer is configured
    // at all but genuinely unreachable, the kitchen has no way to learn
    // about the order (paper tickets only, no digital kitchen display), so
    // placing it anyway would silently lose it. Only blocks when BOTH
    // printers fail this check - one working printer already covers for
    // the other via PrintOrderCoreAsync's fallback. Skips the check
    // entirely (never blocks) when neither printer is configured yet -
    // that intentionally falls through to PrintOrderCoreAsync's
    // preview-only mode. Ported verbatim from OrderTakingControl - do not
    // restructure, see git tag pos-a-local-stable.
    private async Task<bool> BothConfiguredPrintersUnreachableAsync()
    {
        var settings = PrinterSettings.Load();
        var clientConfigured = !string.IsNullOrWhiteSpace(settings.ClientPrinterIp);
        var kitchenConfigured = !string.IsNullOrWhiteSpace(settings.KitchenPrinterIp);

        if (!clientConfigured && !kitchenConfigured) return false;

        var clientReachableTask = clientConfigured
            ? new NetworkReceiptPrinter(settings.ClientPrinterIp, settings.ClientPrinterPort).IsReachableAsync()
            : Task.FromResult(false);
        var kitchenReachableTask = kitchenConfigured
            ? new NetworkReceiptPrinter(settings.KitchenPrinterIp, settings.KitchenPrinterPort).IsReachableAsync()
            : Task.FromResult(false);

        var clientReachable = await clientReachableTask;
        var kitchenReachable = await kitchenReachableTask;

        return !clientReachable && !kitchenReachable;
    }

    // Both printers fire at the same time (not one-then-the-other) so the
    // total wall-clock delay is however long the slower one takes. A
    // failure here never throws back into the cashier flow - the order is
    // already placed and the cart already cleared by the time this runs.
    // Wrapped in a top-level try/catch because this whole method runs
    // fire-and-forget (see the caller above). Ported verbatim - see
    // git tag pos-a-local-stable.
    private async Task PrintOrderAsync(ReceiptOrder order)
    {
        try
        {
            await PrintOrderCoreAsync(order);
        }
        catch (Exception ex)
        {
            ShowStatus($"Print failed: {ex.Message}", success: false);
        }
    }

    private async Task PrintOrderCoreAsync(ReceiptOrder order)
    {
        var settings = PrinterSettings.Load();

        // The real order number stays exactly as-is everywhere else (Order
        // History, reports, the cashier's own on-screen confirmation) -
        // only what gets printed on the two receipts is wrapped, so a
        // customer can't tell how many orders have been placed that day
        // just from their own receipt.
        var printSerial = settings.ReceiptOrderNumberWrapAt > 0
            ? ((order.SerialNumber - 1) % settings.ReceiptOrderNumberWrapAt) + 1
            : order.SerialNumber;
        var printOrder = order with { SerialNumber = printSerial };

        var kitchenTicket = ReceiptBuilder.BuildKitchenTicket(printOrder, settings.KitchenTicketFontSize);
        var customerReceipt = ReceiptBuilder.BuildCustomerReceipt(printOrder, settings.ShowOrderTimeOnReceipt, settings.TaxDisplayMode, settings.ClientReceiptFontSize);

        // No real printer set up yet (see Settings) - show what would have
        // printed for this actual order instead of failing/timing out
        // against an empty address.
        if (string.IsNullOrWhiteSpace(settings.ClientPrinterIp) && string.IsNullOrWhiteSpace(settings.KitchenPrinterIp))
        {
            await Navigation.PushModalAsync(new ReceiptPreviewPage("Client Receipt (no printer configured - preview only)",
                ReceiptBuilder.PreviewCustomerReceipt(printOrder, settings.ShowOrderTimeOnReceipt, settings.TaxDisplayMode, settings.ClientReceiptFontSize)));
            await Navigation.PushModalAsync(new ReceiptPreviewPage("Kitchen Ticket (no printer configured - preview only)",
                ReceiptBuilder.PreviewKitchenTicket(printOrder, settings.KitchenTicketFontSize)));
            return;
        }

        var clientTask = PrintSafelyAsync(settings.ClientPrinterIp, settings.ClientPrinterPort, customerReceipt, "Client receipt");
        var kitchenTask = PrintSafelyAsync(settings.KitchenPrinterIp, settings.KitchenPrinterPort, kitchenTicket, "Kitchen ticket");

        var clientFailure = await clientTask;
        var kitchenFailure = await kitchenTask;

        // Either receipt failing on its own printer falls back to the
        // OTHER printer instead of being lost - as long as one printer is
        // actually up, both receipts end up printed somewhere.
        string? kitchenFallbackFailure = null;
        if (kitchenFailure is not null)
            kitchenFallbackFailure = await PrintSafelyAsync(settings.ClientPrinterIp, settings.ClientPrinterPort, kitchenTicket, "Kitchen ticket (fallback)");

        string? clientFallbackFailure = null;
        if (clientFailure is not null)
            clientFallbackFailure = await PrintSafelyAsync(settings.KitchenPrinterIp, settings.KitchenPrinterPort, customerReceipt, "Client receipt (fallback)");

        var kitchenOk = kitchenFailure is null || kitchenFallbackFailure is null;
        var clientOk = clientFailure is null || clientFallbackFailure is null;

        if (!kitchenOk && !clientOk)
            ShowStatus("Print failed: both printers unreachable.", success: false);
        else if (!kitchenOk)
            ShowStatus($"Print failed: {kitchenFallbackFailure}", success: false);
        else if (!clientOk)
            ShowStatus($"Print failed: {clientFallbackFailure}", success: false);
        else if (kitchenFailure is not null || clientFailure is not null)
            ShowStatus("A printer was unreachable - both receipts printed on the other one.", success: false);
    }

    // Returns null on success, or a short failure label on failure.
    private static async Task<string?> PrintSafelyAsync(string ip, int port, byte[] data, string label)
    {
        if (string.IsNullOrWhiteSpace(ip)) return $"{label} (not configured)";

        try
        {
            await new NetworkReceiptPrinter(ip, port).PrintAsync(data);
            return null;
        }
        catch
        {
            return label;
        }
    }

    private void ShowStatus(string text, bool success)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            PrintStatusLabel.Text = text;
            PrintStatusLabel.TextColor = success ? Color.FromArgb("#198754") : Color.FromArgb("#DC3545");
        });
    }

    private class CartLine(ItemDto item, int quantity = 1)
    {
        public ItemDto Item { get; } = item;
        public int Quantity { get; } = quantity;
        public string? Comment { get; set; }
    }
}
