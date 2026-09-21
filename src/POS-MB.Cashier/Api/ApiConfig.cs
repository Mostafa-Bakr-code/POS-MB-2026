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
    //
    // "localhost" from inside an Android emulator means the emulator
    // itself, not the host PC running it - 10.0.2.2 is the special alias
    // Android's emulator provides specifically to reach the host machine's
    // own localhost (same reasoning as POS-MB.Mobile's ApiConfig, which
    // instead uses a real LAN IP since it also targets real devices - this
    // app's dev/test loop so far is emulator-only, so the simpler alias
    // works without needing to know this PC's actual IP at all).
#if ANDROID
    private const string DefaultBaseUrl = "http://10.0.2.2:5098/";
#else
    private const string DefaultBaseUrl = "http://localhost:5098/";
#endif

    public static string BaseUrl
    {
        get => Preferences.Default.Get(PreferenceKey, DefaultBaseUrl);
        set => Preferences.Default.Set(PreferenceKey, value);
    }
}
