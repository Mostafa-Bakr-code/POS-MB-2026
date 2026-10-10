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
    // Matches SettingsPage's own copy of this key - the setting that marks
    // which category's items show up behind a cart line's "+Extra" button,
    // same reasoning as every other setting key this app keeps its own copy
    // of (see AcceptingOnlineOrdersSettingKey elsewhere).
    private const string ExtrasCategorySettingKey = "ExtrasCategoryId";

    private readonly ApiClient _apiClient = new();

    private List<CategoryDto> _categories = [];
    private readonly List<CartLine> _cart = [];
    private int? _extrasCategoryId;
    private List<ItemDto> _extraItems = [];

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
        await LoadExtrasCategoryAsync();
    }

    // Loaded once per page visit (not re-fetched on every "+Extra" tap) -
    // the extras list is small and rarely changes mid-shift, same tradeoff
    // as LoadCategoriesAsync itself.
    private async Task LoadExtrasCategoryAsync()
    {
        var value = await _apiClient.GetSettingValueAsync(ExtrasCategorySettingKey);
        _extrasCategoryId = int.TryParse(value, out var id) ? id : null;
        _extraItems = _extrasCategoryId is int categoryId
            ? await _apiClient.GetItemsAsync(categoryId, availableOnly: true)
            : [];
    }

    private async Task LoadCategoriesAsync()
    {
        CategoriesLayout.Children.Clear();
        _categories = await _apiClient.GetCategoriesAsync();

        foreach (var category in _categories)
        {
            var button = CreateTileButton(category.CategoryName, null);
            button.Clicked += async (_, _) => await OpenCategoryAsync(category);
            CategoriesLayout.Children.Add(button);
        }
    }

    // Drill-down, not a Navigation.PushAsync page - the cart on the right
    // and the whole page's state need to stay exactly as they are; only
    // which of the two "screens" is visible changes.
    private async Task OpenCategoryAsync(CategoryDto category)
    {
        SelectedCategoryLabel.Text = category.CategoryName;
        await LoadItemsAsync(category.CategoryId);

        CategoriesScreen.IsVisible = false;
        ItemsScreen.IsVisible = true;
    }

    private void OnBackToCategoriesClicked(object? sender, EventArgs e)
    {
        ItemsScreen.IsVisible = false;
        CategoriesScreen.IsVisible = true;
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
        InsertGrouped(_cart, new CartLine(item, quantity));

        if (QuantityStepper.Value != 1) QuantityStepper.Value = 1;

        RenderCart();
    }

    // An extra always attaches to the ONE specific line it was requested
    // for - never to the cart generally - so two separate occurrences of
    // the same base item can carry two different extras without either
    // receipt line being ambiguous about which extra belongs to which.
    private async void OnAddExtraClicked(CartLine parentLine)
    {
        if (_extraItems.Count == 0)
        {
            await UiAlerts.Error(this, _extrasCategoryId is null
                ? "No extras category is configured yet - set one in Settings."
                : "The extras category has no items yet.");
            return;
        }

        var labels = _extraItems.Select(i => $"{i.ItemName} ({i.Price:0.00})").ToArray();
        var choice = await DisplayActionSheet($"Add extra to {parentLine.Item.ItemName}", "Cancel", null, labels);
        if (choice is null || choice == "Cancel") return;

        var index = Array.IndexOf(labels, choice);
        if (index < 0) return;

        InsertGrouped(parentLine.Extras, new CartLine(_extraItems[index], 1, isExtra: true));
        RenderCart();
    }

    // Groups with any existing line(s) for the same item instead of always
    // appending at the bottom - shared by base items (against the whole
    // cart) and extras (against their one parent's own Extras list), same
    // reasoning either way: tapping the same thing again should land right
    // next to its other occurrence(s), not at the very end.
    private static void InsertGrouped(List<CartLine> list, CartLine newLine)
    {
        var lastIndex = list.FindLastIndex(c => c.Item.ItemId == newLine.Item.ItemId);
        if (lastIndex >= 0) list.Insert(lastIndex + 1, newLine);
        else list.Add(newLine);
    }

    private void AddAnother(CartLine existing)
    {
        var list = FindOwningList(existing);
        var index = list.IndexOf(existing);
        list.Insert(index + 1, new CartLine(existing.Item, 1, existing.IsExtra));
        RenderCart();
    }

    // A cart line can live at the top level OR inside some other line's
    // Extras - the +1/X/comment buttons are shared by both via this lookup
    // rather than needing two separate code paths.
    private List<CartLine> FindOwningList(CartLine target)
    {
        if (_cart.Contains(target)) return _cart;
        foreach (var line in _cart)
            if (line.Extras.Contains(target)) return line.Extras;
        return _cart;
    }

    // Flattens into display/order order: each base item immediately
    // followed by its own extras, then the next base item - this is what
    // makes the printed receipt/kitchen ticket unambiguous without needing
    // any new field on the order itself (see OnPlaceOrderClicked).
    private List<CartLine> FlattenCart() =>
        _cart.SelectMany(line => new[] { line }.Concat(line.Extras)).ToList();

    private void RenderCart()
    {
        var flattened = FlattenCart();

        CartView.ItemsSource = null;
        CartView.ItemsSource = flattened;

        var total = flattened.Sum(c => c.Item.Price * c.Quantity);
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
                new ColumnDefinition(new GridLength(50)),
                new ColumnDefinition(new GridLength(40)),
                new ColumnDefinition(new GridLength(40))
            },
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) },
            Padding = new Thickness(4),
            RowSpacing = 2
        };

        var nameLabel = new Label { FontAttributes = FontAttributes.Bold };
        Grid.SetColumn(nameLabel, 0);
        grid.Children.Add(nameLabel);

        var addExtraButton = new Button { Text = "+Extra", FontSize = 10, WidthRequest = 46, HeightRequest = 40, Padding = 0 };
        Grid.SetColumn(addExtraButton, 1);
        grid.Children.Add(addExtraButton);

        var addButton = new Button { Text = "+1", WidthRequest = 36, HeightRequest = 40, Padding = 0 };
        Grid.SetColumn(addButton, 2);
        grid.Children.Add(addButton);

        var removeButton = new Button { Text = "X", WidthRequest = 36, HeightRequest = 40, Padding = 0, TextColor = Colors.Red };
        Grid.SetColumn(removeButton, 3);
        grid.Children.Add(removeButton);

        var commentButton = new Button { FontSize = 12, HorizontalOptions = LayoutOptions.Start };
        Grid.SetRow(commentButton, 1);
        Grid.SetColumn(commentButton, 0);
        Grid.SetColumnSpan(commentButton, 4);
        grid.Children.Add(commentButton);

        grid.BindingContextChanged += (_, _) =>
        {
            if (grid.BindingContext is not CartLine line) return;

            // Indented and prefixed so an extra reads as "belonging to"
            // whichever base item sits directly above it in the cart/
            // receipt, instead of looking like just another top-level line.
            nameLabel.Margin = line.IsExtra ? new Thickness(20, 0, 0, 0) : new Thickness(0);
            var prefix = line.IsExtra ? "+ " : "";
            nameLabel.Text = line.Quantity > 1
                ? $"{prefix}{line.Item.ItemName} x{line.Quantity}  {(line.Item.Price * line.Quantity):0.00}"
                : $"{prefix}{line.Item.ItemName}  {line.Item.Price:0.00}";

            // One level of nesting only - an extra can't itself carry
            // further extras.
            addExtraButton.IsVisible = !line.IsExtra;

            commentButton.Text = string.IsNullOrWhiteSpace(line.Comment)
                ? "Add Comment"
                : $"Comment: {line.Comment}";
        };

        addExtraButton.Clicked += (_, _) =>
        {
            if (grid.BindingContext is CartLine line) OnAddExtraClicked(line);
        };
        addButton.Clicked += (_, _) =>
        {
            if (grid.BindingContext is CartLine line) AddAnother(line);
        };
        removeButton.Clicked += (_, _) =>
        {
            if (grid.BindingContext is CartLine line) { FindOwningList(line).Remove(line); RenderCart(); }
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

            // Flattened, not just _cart - each base item's own extras need
            // to travel to the server as their own OrderItem rows too, sent
            // immediately after their parent (not grouped by name) so the
            // order they come back in later (Order History, a reprint, the
            // kitchen ticket) still reads unambiguously which extra was for
            // which specific item, with no new field needed anywhere.
            var allLines = FlattenCart();

            var request = new CreateOrderRequest(
                OrderSource.Cashier,
                AppSession.CurrentUser.UserId,
                IsComplimentary: ComplimentaryCheck.IsChecked,
                allLines.Select(c => new NewOrderItemRequest(c.Item.ItemId, c.Quantity, c.Comment)).ToList());

            var (order, error) = await _apiClient.CreateOrderAsync(request);
            if (order is null)
            {
                await UiAlerts.Error(this, $"Could not place the order: {error}");
                return;
            }

            // Built from order.Items (what the server actually confirmed and
            // stored), not from the local cart - if a price changed between
            // adding to cart and placing the order, the receipt must match
            // what was really charged. order.Items has no item name, so
            // it's resolved from the cart's own items, whose names can't
            // have changed mid-order.
            var itemNamesById = allLines
                .Select(c => c.Item)
                .DistinctBy(i => i.ItemId)
                .ToDictionary(i => i.ItemId, i => i.ItemName);

            // order.Items comes back in the exact order allLines was sent
            // in (the server just confirms the same list it was given,
            // same reasoning the extras feature itself relies on for the
            // receipt to stay unambiguous) - matched up by position here
            // purely to carry IsExtra across, since that flag only ever
            // existed locally in the cart, never on the server.
            var receiptOrder = new ReceiptOrder(
                order.SerialNumber ?? order.OrderId,
                AppSession.ToLocalDisplay(order.Date),
                order.Items.Select((oi, i) => new ReceiptItem(
                    itemNamesById.GetValueOrDefault(oi.ItemId, "Item"),
                    oi.Quantity, oi.Price, oi.TaxRate, oi.Comment,
                    IsExtra: i < allLines.Count && allLines[i].IsExtra)).ToList(),
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
        var customerReceipt = ReceiptBuilder.BuildCustomerReceipt(printOrder, settings.ReceiptDateTimeDisplay, settings.TaxDisplayMode, settings.ClientReceiptFontSize);

        // No real printer set up yet (see Settings) - show what would have
        // printed for this actual order instead of failing/timing out
        // against an empty address.
        if (string.IsNullOrWhiteSpace(settings.ClientPrinterIp) && string.IsNullOrWhiteSpace(settings.KitchenPrinterIp))
        {
            await Navigation.PushModalAsync(new ReceiptPreviewPage("Client Receipt (no printer configured - preview only)",
                ReceiptBuilder.PreviewCustomerReceipt(printOrder, settings.ReceiptDateTimeDisplay, settings.TaxDisplayMode, settings.ClientReceiptFontSize)));
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

    private class CartLine(ItemDto item, int quantity = 1, bool isExtra = false)
    {
        public ItemDto Item { get; } = item;
        public int Quantity { get; } = quantity;
        public string? Comment { get; set; }

        // True for a line added via another line's "+Extra" button rather
        // than tapped directly off the menu - see Extras below.
        public bool IsExtra { get; } = isExtra;

        // Only ever populated on a non-extra line - one level of nesting,
        // see OnAddExtraClicked's own comment.
        public List<CartLine> Extras { get; } = [];
    }
}
