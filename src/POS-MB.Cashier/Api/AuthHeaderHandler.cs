using System.Net.Http.Headers;
using POS_MB.Cashier.Session;

namespace POS_MB.Cashier.Api;

// Attaches the current session's JWT to every outgoing request, read fresh
// from AppSession on each call rather than captured once - same pattern as
// POS-MB.Mobile's and POS-MB.WinformsApp's own AuthHeaderHandler.
public class AuthHeaderHandler() : DelegatingHandler(new HttpClientHandler())
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (AppSession.Token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AppSession.Token);

        return base.SendAsync(request, cancellationToken);
    }
}
