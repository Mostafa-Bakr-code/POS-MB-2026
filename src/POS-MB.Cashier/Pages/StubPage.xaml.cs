namespace POS_MB.Cashier.Pages;

// Placeholder nav target for any screen not built yet in this phased port -
// lets the main shell's navigation/permission-gating be wired and tested
// end-to-end before every individual screen exists. Each phase replaces its
// corresponding button's target with the real page as it's built.
public partial class StubPage : ContentPage
{
    public StubPage(string title)
    {
        InitializeComponent();
        Title = title;
        TitleLabel.Text = title;
    }
}
