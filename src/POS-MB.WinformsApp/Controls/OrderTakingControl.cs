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

    private List<CategoryDto> _categories = [];
    private List<ItemDto> _items = [];
    private readonly List<CartLine> _cart = [];
    private int? _selectedCategoryId;

    public OrderTakingControl()
    {
        Font = new Font("Segoe UI", 12F);

        _categoryPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 110,
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

        Controls.Add(_itemsPanel);
        Controls.Add(cartContainer);
        Controls.Add(quantityToolbar);
        Controls.Add(_categoryPanel);

        Load += async (_, _) => await LoadCategoriesAsync();
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
        _cart.Add(new CartLine(item, quantity));

        if (_numQuantity.Value != 1) _numQuantity.Value = 1;

        RenderCart();
    }

    private void RenderCart()
    {
        _cartPanel.Controls.Clear();

        foreach (var line in _cart)
        {
            var row = new Panel { Width = _cartPanel.Width - 25, Height = 90, Margin = new Padding(0, 0, 0, 6) };

            var lblName = new Label
            {
                Text = line.Quantity > 1
                    ? $"{line.Item.ItemName} x{line.Quantity}\n{(line.Item.Price * line.Quantity):0.00}"
                    : $"{line.Item.ItemName}\n{line.Item.Price:0.00}",
                Location = new Point(0, 0),
                Size = new Size(150, 60)
            };

            var btnAddOne = new Button { Text = "+", Location = new Point(245, 5), Size = new Size(40, 40), FlatStyle = FlatStyle.Flat };
            btnAddOne.Click += (_, _) => AddToCart(line.Item);

            var btnRemove = new Button { Text = "X", Location = new Point(290, 5), Size = new Size(40, 40), FlatStyle = FlatStyle.Flat, ForeColor = Color.Red };
            btnRemove.Click += (_, _) => { _cart.Remove(line); RenderCart(); };

            var lblComment = new Label
            {
                Text = string.IsNullOrWhiteSpace(line.Comment) ? "No comment" : line.Comment,
                Font = new Font("Segoe UI", 9F, FontStyle.Italic),
                ForeColor = string.IsNullOrWhiteSpace(line.Comment) ? Color.Gray : Color.Black,
                Location = new Point(0, 64),
                Size = new Size(140, 24),
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
            row.Controls.Add(btnAddOne);
            row.Controls.Add(btnRemove);
            row.Controls.Add(lblComment);
            row.Controls.Add(btnComment);

            _cartPanel.Controls.Add(row);
        }

        var total = _cart.Sum(c => c.Item.Price * c.Quantity);
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
            var request = new CreateOrderRequest(
                OrderSource.Cashier,
                AppSession.CurrentUser.UserId,
                IsComplimentary: _chkComplimentary.Checked,
                _cart.Select(c => new NewOrderItemRequest(c.Item.ItemId, c.Quantity, c.Comment)).ToList());

            var order = await _apiClient.CreateOrderAsync(request);

            // Built from order.Items (what the server actually confirmed and
            // stored), not from the local _cart - if a price changed between
            // adding to cart and placing the order, the receipt must match what
            // was really charged, not a stale client-side snapshot. order.Items
            // has no item name, so it's resolved from the cart's own items,
            // whose names can't have changed mid-order.
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
                order.IsComplimentary);

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

    // Both printers fire at the same time (not one-then-the-other) so the total
    // wall-clock delay is however long the slower one takes, not the sum of both.
    // A failure here never throws back into the cashier flow - the order is
    // already placed and the cart already cleared by the time this runs; a
    // printer being offline/out of paper should never block or interrupt the
    // next order, just quietly surface which ticket didn't go out.
    private async Task PrintOrderAsync(ReceiptOrder order)
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

        // Kitchen printer down/unreachable - the order still has to reach the
        // kitchen somehow, so fall back to printing the kitchen ticket on the
        // client printer too (the cashier can hand it over physically) instead
        // of the order silently never reaching the kitchen at all.
        string? fallbackFailure = null;
        var usedFallback = false;
        if (kitchenFailure is not null)
        {
            usedFallback = true;
            fallbackFailure = await PrintSafelyAsync(settings.ClientPrinterIp, settings.ClientPrinterPort,
                kitchenTicket, "Kitchen ticket (fallback)");
        }

        if (clientFailure is not null && kitchenFailure is not null && fallbackFailure is not null)
            ShowStatus("Print failed: both printers unreachable.", success: false);
        else if (clientFailure is not null)
            ShowStatus($"Print failed: {clientFailure}", success: false);
        else if (usedFallback && fallbackFailure is null)
            ShowStatus("Kitchen printer unreachable - ticket printed on client printer instead.", success: false);
        else if (fallbackFailure is not null)
            ShowStatus($"Print failed: {fallbackFailure}", success: false);
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

    private class CartLine(ItemDto item, int quantity = 1)
    {
        public ItemDto Item { get; } = item;
        public int Quantity { get; } = quantity;
        public string? Comment { get; set; }
    }
}
