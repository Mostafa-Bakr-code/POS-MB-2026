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
        // cashier adds e.g. 100 burgers without 100 clicks. Resets to 1 after each
        // add - each unit still becomes its own cart line, so per-unit comments
        // still work for whichever of the 100 need one.
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

        cartContainer.Controls.Add(_cartPanel);
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

    // Every unit is its own cart line (not a shared Quantity on one line) so each
    // one can carry its own comment - 3 marghretas can be "no cheese", "extra
    // spicy", and plain, not one comment shared across all 3.
    private void AddToCart(ItemDto item, int quantity = 1)
    {
        for (var i = 0; i < quantity; i++)
            _cart.Add(new CartLine(item));

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
                Text = $"{line.Item.ItemName}\n{line.Item.Price:0.00}",
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
                using var dialog = new FormTextInputDialog($"Comment for {line.Item.ItemName}", "Comment (e.g. no onions)", line.Comment ?? "");
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

        var total = _cart.Sum(c => c.Item.Price);
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
                _cart.Select(c => new NewOrderItemRequest(c.Item.ItemId, 1, c.Comment)).ToList());

            var order = await _apiClient.CreateOrderAsync(request);

            MessageBox.Show(
                $"Order #{order.SerialNumber} placed successfully.\nTotal: {order.Total:0.00}",
                "Order Placed", MessageBoxButtons.OK, MessageBoxIcon.Information);

            _cart.Clear();
            _chkComplimentary.Checked = false;
            RenderCart();
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

    private class CartLine(ItemDto item)
    {
        public ItemDto Item { get; } = item;
        public string? Comment { get; set; }
    }
}
