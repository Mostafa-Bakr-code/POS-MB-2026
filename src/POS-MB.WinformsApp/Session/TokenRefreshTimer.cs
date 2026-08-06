using System.Threading;

namespace POS_MB.WinformsApp.Session;

// Keeps the access token alive across a long shift by silently renewing it well
// before its ~60 minute lifetime runs out, using the refresh token from login.
// Stops being useful on its own once the refresh token itself expires (1 day of
// inactivity) - at that point refresh calls just start failing, and whatever API
// call runs next will get a 401 like any other expired-session case.
public class TokenRefreshTimer : IDisposable
{
    private readonly System.Threading.Timer _timer;
    private int _isRunning;

    public TokenRefreshTimer(Func<Task> refreshAsync, TimeSpan interval)
    {
        _timer = new System.Threading.Timer(
            async _ =>
            {
                // System.Threading.Timer doesn't wait for a slow callback to finish
                // before scheduling the next tick - on a slow/degraded connection,
                // two overlapping refresh calls can both go out with the SAME
                // still-valid token, and the server-side rotation then reads the
                // loser as a stolen token being replayed (a false "theft detected"
                // alarm, not an actual attack). This skips a tick entirely if the
                // previous one hasn't finished yet, so only one refresh is ever in
                // flight at a time.
                if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0) return;
                try { await refreshAsync(); }
                catch { /* best-effort - the next tick, or an eventual 401, surfaces any real problem */ }
                finally { Interlocked.Exchange(ref _isRunning, 0); }
            },
            null, interval, interval);
    }

    public void Dispose() => _timer.Dispose();
}
