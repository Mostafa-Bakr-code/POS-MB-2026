namespace POS_MB.Cashier.Api;

// Unlike POS-MB.Mobile's ApiConfig (one hardcoded dev-machine IP, since it
// only ever runs against this project's own dev server), this app gets
// deployed to a different network per restaurant - the base URL has to be
// something an installer can set on the device itself, not something baked
// into the build. Stored via Preferences (not SecureStorage - it's a plain
// server address, not a secret, same distinction the refresh token's
// SecureStorage use makes elsewhere).
public static class ApiConfig
{
    private const string PreferenceKey = "ApiBaseUrl";

    // A same-network default so a fresh dev/test install still points
    // somewhere sensible before an installer sets the real address - not
    // meant to be the production value for any real deployment.
    private const string DefaultBaseUrl = "http://localhost:5098/";

    public static string BaseUrl
    {
        get => Preferences.Default.Get(PreferenceKey, DefaultBaseUrl);
        set => Preferences.Default.Set(PreferenceKey, value);
    }
}
