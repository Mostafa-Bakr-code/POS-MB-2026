namespace POS_MB.Mobile.Api;

public static class ApiConfig
{
    // The Android emulator runs as its own virtual machine - "localhost" from
    // its perspective means the emulator itself, not the Windows PC hosting it.
    // 10.0.2.2 is a special alias Android's emulator provides specifically to
    // reach the host machine's localhost. iOS's simulator (unlike Android's)
    // shares the host's network directly, so it can use localhost as-is - no
    // special-casing needed there when we get to it.
#if ANDROID
    public const string BaseUrl = "https://10.0.2.2:7295/";
#else
    public const string BaseUrl = "https://localhost:7295/";
#endif
}
