using System.Net.Http.Headers;
using POS_MB.Web.Session;

namespace POS_MB.Web.Api;

// Mirrors POS-MB.Mobile's Api/AuthHeaderHandler.cs exactly - attaches the
// current session's JWT to every outgoing request, read fresh from
// AppSession on each call rather than captured once.
public class AuthHeaderHandler() : DelegatingHandler(new HttpClientHandler())
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (AppSession.Token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AppSession.Token);

        return base.SendAsync(request, cancellationToken);
    }
}
