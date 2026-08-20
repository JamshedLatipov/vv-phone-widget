using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace OrbitalSIP.Services;

/// <summary>
/// Swaps a spent bearer token for a fresh one before the request goes out, and reports
/// the 401s that survive that.
///
/// Sits in the pipeline rather than in each service because every one of them builds its
/// own <c>HttpRequestMessage</c> and stamps <c>Authorization</c> from
/// <c>settings.AccessToken</c> by hand — there are a dozen such call sites and no shared
/// request builder to hang this off instead.
///
/// Refreshing up front rather than retrying on 401 is what keeps this small: replaying a
/// request means cloning it, and cloning means buffering every request body on the
/// chance it might be needed twice. See <see cref="BackendAuth"/> for why a surviving
/// 401 is not worth a retry.
/// </summary>
internal sealed class AuthRefreshHandler : DelegatingHandler
{
    public AuthRefreshHandler(HttpMessageHandler innerHandler) : base(innerHandler)
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var carriesToken = request.Headers.Authorization?.Scheme == "Bearer";

        if (carriesToken)
        {
            var fresh = await BackendAuth.EnsureFreshAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(fresh))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fresh);
        }

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (carriesToken && response.StatusCode == HttpStatusCode.Unauthorized)
            BackendAuth.NotifyUnauthorized();

        return response;
    }
}
