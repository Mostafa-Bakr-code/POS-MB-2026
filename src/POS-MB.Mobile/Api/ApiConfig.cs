namespace POS_MB.Mobile.Api;

public static class ApiConfig
{
    // The Android emulator runs as its own virtual machine - "localhost" from
    // its perspective means the emulator itself, not the Windows PC hosting it.
    // 10.0.2.2 is a special alias Android's emulator provides specifically to
    // reach the host machine's localhost.
    //
    // A REAL device (Android or iOS) isn't running on this PC at all - it
    // needs the PC's actual LAN IP address, and both devices need to be on
    // the same WiFi. This build is deployed via a paired Mac over the
    // network, not running locally, so IOS gets the same LAN-IP treatment as
    // ANDROID (the iOS Simulator would also reach this fine, since the Mac
    // itself is on the same LAN). Plain HTTP, not HTTPS: a real device
    // doesn't trust this PC's self-signed ASPNET dev certificate, and
    // installing it just for a look-and-feel test isn't worth the extra
    // setup - fine for local testing, never for anything real.
#if ANDROID || IOS
    public const string BaseUrl = "http://192.168.1.3:5098/";
#else
    public const string BaseUrl = "https://localhost:7295/";
#endif
}
