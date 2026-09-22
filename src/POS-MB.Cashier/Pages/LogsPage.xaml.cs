using POS_MB.Cashier.Api;
using POS_MB.Cashier.Controls;
using POS_MB.Cashier.Models;
using POS_MB.Cashier.Session;

namespace POS_MB.Cashier.Pages;

// Replaces LogsControl - shift/session history (who logged in, when, and
// for how long). Sorting deliberately not ported (same reasoning as the
// other grid screens).
public partial class LogsPage : ContentPage
{
    private readonly ApiClient _apiClient = new();

    public LogsPage()
    {
        InitializeComponent();

        StartDatePicker.Date = DateTime.Today;
        EndDatePicker.Date = DateTime.Today;

        LogsGrid.SetColumns(
        [
            new GridColumn("User", r => ((LogRow)r).UserName, weight: 1.4),
            new GridColumn("Log In", r => AppSession.ToLocalDisplay(((LogRow)r).LogIn).ToString("yyyy-MM-dd HH:mm"), weight: 1.2),
            new GridColumn("Log Out", r => ((LogRow)r).LogOut is DateTime logOut ? AppSession.ToLocalDisplay(logOut).ToString("yyyy-MM-dd HH:mm") : "", weight: 1.2),
            new GridColumn("Duration", r => ((LogRow)r).Duration, weight: 0.9)
        ]);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    private void OnUseDateRangeToggled(object? sender, ToggledEventArgs e)
    {
        StartDatePicker.IsEnabled = UseDateRangeSwitch.IsToggled;
        EndDatePicker.IsEnabled = UseDateRangeSwitch.IsToggled;
    }

    private async void OnRefreshClicked(object? sender, EventArgs e) => await LoadAsync();

    private async Task LoadAsync()
    {
        DateTime? start = UseDateRangeSwitch.IsToggled ? StartDatePicker.Date : null;
        DateTime? end = UseDateRangeSwitch.IsToggled ? EndDatePicker.Date : null;

        if (start is not null && end is not null && end < start)
        {
            await UiAlerts.Error(this, "End date cannot be before start date.");
            return;
        }

        var logs = await _apiClient.GetLogsAsync(start, end);
        var users = await _apiClient.GetUsersAsync(includeInactive: true);
        var userNamesById = users.ToDictionary(u => u.UserId, u => u.UserName);

        var rows = logs.Select(log => new LogRow
        {
            UserName = userNamesById.GetValueOrDefault(log.UserId, "(unknown)"),
            LogIn = log.LogIn,
            LogOut = log.LogOut,
            Duration = log.LogOut is DateTime logOut ? FormatDuration(logOut - log.LogIn) : "(active)"
        }).ToList();

        LogsGrid.ItemsSource = rows.Cast<object>();
    }

    private static string FormatDuration(TimeSpan span) =>
        span.TotalHours >= 1
            ? $"{(int)span.TotalHours}h {span.Minutes}m"
            : $"{span.Minutes}m";

    private class LogRow
    {
        public string UserName { get; set; } = string.Empty;
        public DateTime LogIn { get; set; }
        public DateTime? LogOut { get; set; }
        public string Duration { get; set; } = string.Empty;
    }
}
