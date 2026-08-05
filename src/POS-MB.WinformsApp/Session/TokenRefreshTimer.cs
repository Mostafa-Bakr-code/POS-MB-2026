namespace POS_MB.WinformsApp.Session;

// Keeps the access token alive across a long shift by silently renewing it well
// before its ~60 minute lifetime runs out, using the refresh token from login.
// Stops being useful on its own once the refresh token itself expires (1 day of
// inactivity) - at that point refresh calls just start failing, and whatever API
// call runs next will get a 401 like any other expired-session case.
public class TokenRefreshTimer : IDisposable
{
    private readonly System.Threading.Timer _timer;

    public TokenRefreshTimer(Func<Task> refreshAsync, TimeSpan interval)
    {
        _timer = new System.Threading.Timer(
            async _ =>
            {
                try { await refreshAsync(); }
                catch { /* best-effort - the next tick, or an eventual 401, surfaces any real problem */ }
            },
            null, interval, interval);
    }

    public void Dispose() => _timer.Dispose();
}
