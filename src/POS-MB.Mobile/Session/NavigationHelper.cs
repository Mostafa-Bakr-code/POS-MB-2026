namespace POS_MB.Mobile.Session;

public static class NavigationHelper
{
    // "Home" for a logged-in student is MenuPage, not the actual root of the
    // navigation stack - LoginPage (index 0) sits underneath it and stays
    // there for the app's normal silent-relogin flow, so it's never
    // something a student should land back on by tapping a "Home" button.
    // Works from any depth: removes everything between MenuPage and the
    // current page silently, then pops the current page with its normal
    // back-transition, revealing MenuPage underneath.
    public static async Task GoHomeAsync(INavigation navigation)
    {
        var stack = navigation.NavigationStack;
        for (var i = stack.Count - 2; i >= 2; i--)
            navigation.RemovePage(stack[i]);

        if (stack.Count > 1)
            await navigation.PopAsync();
    }
}
