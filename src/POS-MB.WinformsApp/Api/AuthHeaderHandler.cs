using System.Net.Http.Headers;
using POS_MB.WinformsApp.Session;

namespace POS_MB.WinformsApp.Api;

// Attaches the current session's JWT to every outgoing request, read fresh from
// AppSession on each call rather than captured once - every open form/control
// has its own ApiClient/HttpClient instance, so this is what lets all of them
// pick up a freshly issued token (or lose one on logout) without recreating
// anything.
public class AuthHeaderHandler() : DelegatingHandler(new HttpClientHandler())
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (AppSession.Token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AppSession.Token);

        return base.SendAsync(request, cancellationToken);
    }
}
