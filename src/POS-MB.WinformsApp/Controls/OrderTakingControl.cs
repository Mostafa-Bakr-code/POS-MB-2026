using POS_MB.Printing;
using POS_MB.WinformsApp.Api;
using POS_MB.WinformsApp.Dialogs;
using POS_MB.WinformsApp.Models;
using POS_MB.WinformsApp.Session;

namespace POS_MB.WinformsApp.Controls;

public class OrderTakingControl : UserControl
{
    private readonly ApiClient _apiClient = new();

    private readonly FlowLayoutPanel _categoryPanel;
    private readonly NumericUpDown _numQuantity;
    private readonly FlowLayoutPanel _itemsPanel;
    private readonly FlowLayoutPanel _cartPanel;
    private readonly Label _lblTotal;
    private readonly CheckBox _chkComplimentary;
    private readonly Button _btnPlaceOrder;
    private readonly Label _lblPrintStatus;

    // A long item name (e.g. "pizza supersupreme") wraps inside the cart
    // row's fixed-height name label and pushes the price line below it
    // out of view, clipped rather than shown - this surfaces the full
    // name-and-price text on hover so nothing is ever silently hidden.
    private readonly ToolTip _cartTooltip = new();

    // Matches the Cashier/Mobile apps' own copy of this key - the setting
    // that marks which category's items show up behind a cart line's
    // "+Extra" button.
    private const string ExtrasCategorySettingKey = "ExtrasCategoryId";

    private List<CategoryDto> _categories = [];
    private List<ItemDto> _items = [];
    private readonly List<CartLine> _cart = [];
    private int? _selectedCategoryId;
    private int? _extrasCategoryId;
    private List<ItemDto> _extraItems = [];

    public OrderTakingControl()
    {
        Font = new Font("Segoe UI", 12F);

        _categoryPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            // Found live: with this panel only tall enough for one row, too
            // many categories wrapped onto a second row (FlowLayoutPanel's
            // default WrapContents=true) that got clipped below the visible
            // strip - reachable only by scrolling inside a thin area nobody
            // would think to scroll, which looked like they'd vanished.
            // Tall enough for two full rows (each ~86px: an 80px button plus
            // its default 3px margin) plus this panel's own padding: a third
            // row, if it ever comes to that, is still reachable via
            // AutoScroll below.
            Height = 200,
            AutoScroll = true,
            Padding = new Padding(10)
        };

        // Typing a quantity here (instead of tapping a tile N times) is how a
        // cashier adds e.g. 100 burgers without 100 clicks - collapses into one
        // "Burger x100" line with one shared comment, since 100 individually
        // commentable lines would be unusable on a receipt. Tapping a tile directly
        // (or the "+" on a cart row) still always adds a separate quantity-1 line,
        // so per-unit comments stay available for small orders that need them.
        var quantityToolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 56, Padding = new Padding(10, 8, 10, 8) };
        var lblQuantity = new Label { Text = "Quantity to add:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 10, 10, 0) };
        _numQuantity = new NumericUpDown { Width = 90, Height = 36, Minimum = 1, Maximum = 999, Value = 1, Font = new Font("Segoe UI", 12F) };
        quantityToolbar.Controls.Add(lblQuantity);
        quantityToolbar.Controls.Add(_numQuantity);

        var cartContainer = new Panel
        {
            Dock = DockStyle.Right,
            Width = 400,
            Padding = new Padding(10),
            BackColor = Color.FromArgb(248, 249, 250)
        };

        var lblCartTitle = new Label
        {
            Text = "Current Order",
            Dock = DockStyle.Top,
            Height = 40,
            Font = new Font("Segoe UI", 16F, FontStyle.Bold)
        };

        _cartPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true
        };

        _lblTotal = new Label
        {
            Text = "Total: 0.00",
            Dock = DockStyle.Bottom,
            Height = 50,
            Font = new Font("Segoe UI", 16F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };

        var canMarkComplimentary = AppSession.HasPermission(Permission.Complimentary);
        _chkComplimentary = new CheckBox
        {
            Text = canMarkComplimentary ? "Complimentary Order (no charge)" : "Complimentary Order (no permission)",
            Dock = DockStyle.Bottom,
            Height = 40,
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            ForeColor = Color.FromArgb(220, 53, 69),
            Enabled = canMarkComplimentary
        };

        _btnPlaceOrder = new Button
        {
            Text = "Place Order",
            Dock = DockStyle.Bottom,
            Height = 70,
            Font = new Font("Segoe UI", 14F, FontStyle.Bold),
            BackColor = Color.FromArgb(25, 135, 84),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _btnPlaceOrder.Click += BtnPlaceOrder_Click;

        // Printing happens in the background right after an order is placed (see
        // BtnPlaceOrder_Click) - the cashier is never blocked waiting on it. This
        // label is just a quiet, non-blocking way to surface "a printer didn't
        // respond" without interrupting the next order with a popup.
        _lblPrintStatus = new Label
        {
            Text = "",
            Dock = DockStyle.Bottom,
            Height = 26,
            Font = new Font("Segoe UI", 9F),
            ForeColor = Color.FromArgb(220, 53, 69),
            TextAlign = ContentAlignment.MiddleCenter
        };

        cartContainer.Controls.Add(_cartPanel);
        cartContainer.Controls.Add(_lblPrintStatus);
        cartContainer.Controls.Add(_lblTotal);
        cartContainer.Controls.Add(_chkComplimentary);
        cartContainer.Controls.Add(_btnPlaceOrder);
        cartContainer.Controls.Add(lblCartTitle);

        _itemsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(10)
        };

        // Docking order matters here: WinForms docks controls in reverse
        // Controls.Add order (the last one added claims its edge first).
        // Found live: with cartContainer added before the Top-docked
        // category/quantity panels, those two claimed the FULL width above
        // it first, pushing the cart's own top edge down whenever the
        // category panel grew taller (e.g. wrapping to two rows) - making
        // "Current Order" visually shrink even though nothing about it
        // changed. Adding cartContainer LAST instead gives its Dock.Right
        // claim top priority, so it always spans the full height on the
        // right; the category panel, quantity toolbar and item grid then
        // only ever share the remaining column to its left.
        Controls.Add(_itemsPanel);
        Controls.Add(quantityToolbar);
        Controls.Add(_categoryPanel);
        Controls.Add(cartContainer);

        Load += async (_, _) =>
        {
            await LoadCategoriesAsync();
            await LoadExtrasCategoryAsync();
        };
    }

    // Loaded once per control load, not re-fetched on every "+Extra" click -
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
        _categoryPanel.Controls.Clear();
        _categories = await _apiClient.GetCategoriesAsync();

        foreach (var category in _categories)
        {
            var button = CreateTileButton(category.CategoryName, null, 140, 80);
            button.Tag = category.CategoryId;
            button.Click += async (s, _) =>
            {
                _selectedCategoryId = category.CategoryId;
                HighlightSelectedCategory((Button)s!);
                await LoadItemsAsync(category.CategoryId);
            };
            _categoryPanel.Controls.Add(button);
        }

        if (_categories.Count > 0)
        {
            _selectedCategoryId = _categories[0].CategoryId;
            HighlightSelectedCategory((Button)_categoryPanel.Controls[0]);
            await LoadItemsAsync(_categories[0].CategoryId);
        }
    }

    private void HighlightSelectedCategory(Button selected)
    {
        foreach (Button button in _categoryPanel.Controls)
            button.BackColor = button == selected ? Color.FromArgb(13, 110, 253) : Color.FromArgb(233, 236, 239);
        foreach (Button button in _categoryPanel.Controls)
            button.ForeColor = button == selected ? Color.White : Color.Black;
    }

    private async Task LoadItemsAsync(int categoryId)
    {
        _itemsPanel.Controls.Clear();
        _items = await _apiClient.GetItemsAsync(categoryId, availableOnly: true);

        foreach (var item in _items)
        {
            var button = CreateTileButton(item.ItemName, item.Price.ToString("0.00"), 160, 110);
            button.Click += (_, _) => AddToCart(item, (int)_numQuantity.Value);
            _itemsPanel.Controls.Add(button);
        }
    }

    private static Button CreateTileButton(string title, string? subtitle, int width, int height)
    {
        var button = new Button
        {
            Width = width,
            Height = height,
            Margin = new Padding(8),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(233, 236, 239),
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            Text = subtitle is null ? title : $"{title}\n{subtitle}"
        };
        button.FlatAppearance.BorderSize = 1;
        return button;
    }

    // Tapping a tile (quantity == 1, the common case) always adds its own separate
    // line so it can carry its own comment - 3 individual taps means 3 marghretas
    // that can each be "no cheese", "extra spicy", and plain. Typing a bulk quantity
    // first (quantity > 1) instead adds ONE line with that quantity - "Burger x100"
    // with one shared comment, not 100 unreadable lines.
    private void AddToCart(ItemDto item, int quantity = 1)
    {
        InsertGrouped(_cart, new CartLine(item, quantity));

        if (_numQuantity.Value != 1) _numQuantity.Value = 1;

        RenderCart();
    }

    // An extra always attaches to the ONE specific line it was requested
    // for - never to the cart generally - so two separate occurrences of
    // the same base item can carry two different extras without either
    // receipt line being ambiguous about which extra belongs to which.
    private void OnAddExtraClicked(CartLine parentLine)
    {
        if (_extraItems.Count == 0)
        {
            MessageBox.Show(
                _extrasCategoryId is null
                    ? "No extras category is configured yet - set one in Settings."
                    : "The extras category has no items yet.",
                "Extras", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // A proper modal dialog, not the original ContextMenuStrip - a
        // grid of tiles (same look as the category/item tiles above) reads
        // faster and is easier to tap accurately than a vertical text
        // list. ShowDialog blocks until closed, so `using` disposal here
        // is safe (unlike ContextMenuStrip.Show, which isn't blocking).
        using var dialog = new FormExtrasPickerDialog($"Add extra to {parentLine.Item.ItemName}", _extraItems);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.SelectedItem is null) return;

        InsertGrouped(parentLine.Extras, new CartLine(dialog.SelectedItem, 1, isExtra: true));
        RenderCart();
    }

    // Groups with any existing line(s) for the same item instead of always
    // appending at the bottom - shared by base items (against the whole
    // cart) and extras (against their one parent's own Extras list), same
    // reasoning either way: re-tapping the same thing should land right
    // next to its other occurrence(s), not at the very end.
    private static void InsertGrouped(List<CartLine> list, CartLine newLine)
    {
        var lastIndex = list.FindLastIndex(c => c.Item.ItemId == newLine.Item.ItemId);
        if (lastIndex >= 0)
            list.Insert(lastIndex + 1, newLine);
        else
            list.Add(newLine);
    }

    // The cart's own "+1" button inserts right after the line it was pressed
    // on, not at the bottom - so the kitchen sees same-item lines grouped
    // together and can prepare them as a batch instead of hunting for the
    // matching item scattered across the ticket. Works for an extra line
    // too, via FindOwningList - it lands in that same extra's parent, not
    // at the top level.
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
    // any new field on the order itself (see BtnPlaceOrder_Click).
    private List<CartLine> FlattenCart() =>
        _cart.SelectMany(line => new[] { line }.Concat(line.Extras)).ToList();

    private void RenderCart()
    {
        _cartPanel.Controls.Clear();

        foreach (var line in FlattenCart())
        {
            var row = new Panel { Width = _cartPanel.Width - 25, Height = 90, Margin = new Padding(0, 0, 0, 6) };

            // Indented and prefixed, so an extra reads as "belonging to"
            // whichever base item sits directly above it in the cart/
            // receipt, instead of looking like just another top-level line.
            var nameX = line.IsExtra ? 20 : 0;
            var namePrefix = line.IsExtra ? "+ " : "";
            var nameText = line.Quantity > 1
                ? $"{namePrefix}{line.Item.ItemName} x{line.Quantity}\n{(line.Item.Price * line.Quantity):0.00}"
                : $"{namePrefix}{line.Item.ItemName}\n{line.Item.Price:0.00}";
            var lblName = new Label
            {
                Text = nameText,
                Location = new Point(nameX, 0),
                Size = new Size(150 - nameX, 60)
            };
            // A long name (e.g. "pizza supersupreme") wraps and pushes the
            // price line out of this label's fixed height, clipped rather
            // than shown - the tooltip is the one place the full text is
            // always visible regardless of how much room wrapped.
            _cartTooltip.SetToolTip(lblName, nameText);

            // One level of nesting only - an extra can't itself carry
            // further extras, so it gets no "+Extra" button of its own.
            // Bigger than the other row buttons - a cashier taps this one
            // the most, mid-rush, and needs to hit it fast without aiming.
            var btnAddExtra = new Button { Text = "+Extra", Location = new Point(155, 3), Size = new Size(72, 44), FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 10F, FontStyle.Bold), Visible = !line.IsExtra };
            btnAddExtra.Click += (_, _) => OnAddExtraClicked(line);

            var btnAddOne = new Button { Text = "+", Location = new Point(245, 5), Size = new Size(40, 40), FlatStyle = FlatStyle.Flat };
            btnAddOne.Click += (_, _) => AddAnother(line);

            var btnRemove = new Button { Text = "X", Location = new Point(290, 5), Size = new Size(40, 40), FlatStyle = FlatStyle.Flat, ForeColor = Color.Red };
            btnRemove.Click += (_, _) => { FindOwningList(line).Remove(line); RenderCart(); };

            var lblComment = new Label
            {
                Text = string.IsNullOrWhiteSpace(line.Comment) ? "No comment" : line.Comment,
                Font = new Font("Segoe UI", 9F, FontStyle.Italic),
                ForeColor = string.IsNullOrWhiteSpace(line.Comment) ? Color.Gray : Color.Black,
                Location = new Point(nameX, 64),
                Size = new Size(140 - nameX, 24),
                AutoEllipsis = true
            };
            var btnComment = new Button
            {
                Text = string.IsNullOrWhiteSpace(line.Comment) ? "Add Comment" : "Edit Comment",
                Location = new Point(145, 58),
                Size = new Size(170, 30),
                Font = new Font("Segoe UI", 9F),
                FlatStyle = FlatStyle.Flat
            };
            btnComment.Click += (_, _) =>
            {
                using var dialog = new FormTextInputDialog($"Comment for {line.Item.ItemName}", "Comment (e.g. no onions)", line.Comment ?? "", maxLength: 50);
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    line.Comment = dialog.Value.Length == 0 ? null : dialog.Value;
                    RenderCart();
                }
            };

            row.Controls.Add(lblName);
            row.Controls.Add(btnAddExtra);
            row.Controls.Add(btnAddOne);
            row.Controls.Add(btnRemove);
            row.Controls.Add(lblComment);
            row.Controls.Add(btnComment);

            _cartPanel.Controls.Add(row);
        }

        var total = FlattenCart().Sum(c => c.Item.Price * c.Quantity);
        _lblTotal.Text = $"Total: {total:0.00}";
        _btnPlaceOrder.Enabled = _cart.Count > 0;
    }

    private async void BtnPlaceOrder_Click(object? sender, EventArgs e)
    {
        if (_cart.Count == 0) return;
        if (AppSession.CurrentUser is null)
        {
            MessageBox.Show("No active user session.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _btnPlaceOrder.Enabled = false;
        try
        {
            if (await BothConfiguredPrintersUnreachableAsync())
            {
                MessageBox.Show(
                    "Cannot place this order: both the client and kitchen printers are unreachable. " +
                    "Check the printers and network connection, then try again.",
                    "Printers Offline", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                IsComplimentary: _chkComplimentary.Checked,
                allLines.Select(c => new NewOrderItemRequest(c.Item.ItemId, c.Quantity, c.Comment)).ToList());

            var order = await _apiClient.CreateOrderAsync(request);

            // Built from order.Items (what the server actually confirmed and
            // stored), not from the local cart - if a price changed between
            // adding to cart and placing the order, the receipt must match what
            // was really charged, not a stale client-side snapshot. order.Items
            // has no item name, so it's resolved from the cart's own items,
            // whose names can't have changed mid-order.
            var itemNamesById = allLines
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
            _chkComplimentary.Checked = false;
            RenderCart();

            // Fired in the background, not awaited here - the cashier moves on to
            // the next order immediately instead of waiting on two printers.
            _ = PrintOrderAsync(receiptOrder);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not place the order: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnPlaceOrder.Enabled = true;
        }
    }

    // Checked before an order is even created - if a printer is configured
    // at all but genuinely unreachable, the kitchen has no way to learn
    // about the order (paper tickets only, no digital kitchen display), so
    // placing it anyway would silently lose it. Only blocks when BOTH
    // printers fail this check - one working printer already covers for the
    // other via PrintOrderCoreAsync's fallback, so a single printer hiccup
    // never has to stop the cashier from taking orders. Skips the check
    // entirely (never blocks) when neither printer is configured yet - that
    // intentionally falls through to PrintOrderCoreAsync's preview-only mode.
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

    // Both printers fire at the same time (not one-then-the-other) so the total
    // wall-clock delay is however long the slower one takes, not the sum of both.
    // A failure here never throws back into the cashier flow - the order is
    // already placed and the cart already cleared by the time this runs; a
    // printer being offline/out of paper should never block or interrupt the
    // next order, just quietly surface which ticket didn't go out.
    // Wrapped in a top-level try/catch because this whole method runs
    // fire-and-forget (see the caller above) - anything thrown before the
    // first real await (a corrupted printer-settings.json, a disposed
    // preview dialog) would otherwise be swallowed into a Task nobody
    // observes, silently skipping the kitchen ticket with no error shown at
    // all instead of the clear failure status every other print problem here
    // already surfaces via ShowStatus.
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
        // History, reports, the cashier's own on-screen confirmation) - only what
        // gets printed on the two receipts is wrapped, so a customer can't tell
        // how many orders have been placed that day just from their own receipt.
        var printSerial = settings.ReceiptOrderNumberWrapAt > 0
            ? ((order.SerialNumber - 1) % settings.ReceiptOrderNumberWrapAt) + 1
            : order.SerialNumber;
        var printOrder = order with { SerialNumber = printSerial };

        var kitchenTicket = ReceiptBuilder.BuildKitchenTicket(printOrder, settings.KitchenTicketFontSize);
        var customerReceipt = ReceiptBuilder.BuildCustomerReceipt(printOrder, settings.ShowOrderTimeOnReceipt, settings.TaxDisplayMode, settings.ClientReceiptFontSize);

        // No real printer set up yet (see Settings) - show what would have
        // printed for this actual order instead of failing/timing out against an
        // empty address. Once a printer IP is configured, this stops happening
        // automatically and a real print is attempted instead.
        if (string.IsNullOrWhiteSpace(settings.ClientPrinterIp) && string.IsNullOrWhiteSpace(settings.KitchenPrinterIp))
        {
            using var clientPreview = new FormReceiptPreviewDialog("Client Receipt (no printer configured - preview only)",
                ReceiptBuilder.PreviewCustomerReceipt(printOrder, settings.ShowOrderTimeOnReceipt, settings.TaxDisplayMode, settings.ClientReceiptFontSize));
            clientPreview.ShowDialog(this);

            using var kitchenPreview = new FormReceiptPreviewDialog("Kitchen Ticket (no printer configured - preview only)",
                ReceiptBuilder.PreviewKitchenTicket(printOrder, settings.KitchenTicketFontSize));
            kitchenPreview.ShowDialog(this);
            return;
        }

        var clientTask = PrintSafelyAsync(settings.ClientPrinterIp, settings.ClientPrinterPort,
            customerReceipt, "Client receipt");
        var kitchenTask = PrintSafelyAsync(settings.KitchenPrinterIp, settings.KitchenPrinterPort,
            kitchenTicket, "Kitchen ticket");

        var clientFailure = await clientTask;
        var kitchenFailure = await kitchenTask;

        // Either receipt failing on its own printer falls back to the OTHER
        // printer instead of being lost - the kitchen ticket especially has
        // to reach the kitchen somehow, but there's no reason the customer
        // receipt can't do the same. As long as at least one printer is
        // actually up, both receipts end up printed somewhere.
        string? kitchenFallbackFailure = null;
        if (kitchenFailure is not null)
            kitchenFallbackFailure = await PrintSafelyAsync(settings.ClientPrinterIp, settings.ClientPrinterPort,
                kitchenTicket, "Kitchen ticket (fallback)");

        string? clientFallbackFailure = null;
        if (clientFailure is not null)
            clientFallbackFailure = await PrintSafelyAsync(settings.KitchenPrinterIp, settings.KitchenPrinterPort,
                customerReceipt, "Client receipt (fallback)");

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

    // Returns null on success, or a short failure label on failure - avoids
    // multiple concurrent tasks writing into a shared, non-thread-safe collection.
    private static async Task<string?> PrintSafelyAsync(string ip, int port, byte[] data, string label)
    {
        if (string.IsNullOrWhiteSpace(ip))
            return $"{label} (not configured)";

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
        void Apply()
        {
            _lblPrintStatus.Text = text;
            _lblPrintStatus.ForeColor = success ? Color.FromArgb(25, 135, 84) : Color.FromArgb(220, 53, 69);
        }

        if (InvokeRequired) BeginInvoke(Apply);
        else Apply();
    }

    private class CartLine(ItemDto item, int quantity = 1, bool isExtra = false)
    {
        public ItemDto Item { get; } = item;
        public int Quantity { get; } = quantity;
        public string? Comment { get; set; }

        // True for a line added via another line's "+Extra" button rather
        // than tapped directly off the menu - see OnAddExtraClicked below.
        public bool IsExtra { get; } = isExtra;

        // Only ever populated on a non-extra line - one level of nesting,
        // see OnAddExtraClicked's own comment.
        public List<CartLine> Extras { get; } = [];
    }
}
