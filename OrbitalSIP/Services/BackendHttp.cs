using System;
using System.Net.Http;
using System.Threading;

namespace OrbitalSIP.Services;

/// <summary>
/// The one connection pool for calls to the CRM backend.
///
/// Nine call sites — six services and three views — each built their own
/// HttpClient over its own handler, so nine pools opened nine sets of sockets and
/// ran nine TLS handshakes against the same host, and none of them could reuse a
/// connection another had already warmed. Services created per dialog paid that
/// cost again every time the dialog opened.
///
/// The pool belongs to the handler, not to the HttpClient, so callers that need a
/// different timeout can still get their own client over this same pool through
/// <see cref="CreateClient"/> without splitting the sockets again.
/// </summary>
public static class BackendHttp
{
    /// <summary>
    /// Bounds how long a pooled connection is reused, so a backend that moves to a
    /// new address is picked up. A process-lifetime HttpClient otherwise pins the
    /// address it first resolved for as long as the app runs, and this one runs all day.
    /// </summary>
    private static readonly TimeSpan ConnectionLifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The socket pool. Carries no SslOptions: the callback that used to sit here
    /// accepted every certificate unconditionally, and it was never doing any work
    /// anyway — the deployed BackendUrl is plain http://, so no handshake ever reached
    /// it. Leaving it in place meant the day the backend moved to https, it would have
    /// silently downgraded that to no protection at all. Default validation now applies,
    /// and <see cref="WarnIfInsecure"/> covers the http:// case that is live today.
    /// </summary>
    private static readonly SocketsHttpHandler Transport = new()
    {
        PooledConnectionLifetime = ConnectionLifetime,
    };

    /// <summary>
    /// Bypasses <see cref="AuthRefreshHandler"/>. The token refresh POST has to go out
    /// over something that will not turn around and ask for a token refresh.
    /// </summary>
    internal static HttpClient RawClient { get; } = new(Transport, disposeHandler: false);

    /// <summary>Shared by every client below, so one refresh serves all of them.</summary>
    private static readonly AuthRefreshHandler AuthHandler = new(Transport);

    /// <summary>Shared client for backend calls that are happy with the default timeout.</summary>
    public static HttpClient Client { get; } = new(AuthHandler, disposeHandler: false);

    /// <summary>
    /// A client with its own timeout, over the shared pool. Disposing it releases the
    /// client only — the handler and its sockets stay up for everyone else.
    /// </summary>
    public static HttpClient CreateClient(TimeSpan timeout) =>
        new(AuthHandler, disposeHandler: false) { Timeout = timeout };

    private static int _insecureWarned;

    /// <summary>True when the backend is configured over plain HTTP.</summary>
    public static bool IsInsecure(string? backendUrl) =>
        !string.IsNullOrWhiteSpace(backendUrl) &&
        backendUrl.TrimStart().StartsWith("http://", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Records, once per run, that everything this pool carries — the login POST, the
    /// SIP password from /api/auth/sip-credentials, the bearer token on every later
    /// request, and caller PII — is travelling in cleartext. Nothing the client can do
    /// fixes that; it takes an https listener on the backend. The log line is so the
    /// fact stays visible instead of living only in a review document.
    /// </summary>
    public static void WarnIfInsecure(string? backendUrl)
    {
        if (!IsInsecure(backendUrl)) return;
        if (Interlocked.Exchange(ref _insecureWarned, 1) != 0) return;

        AppLogger.Log("BackendHttp",
            $"INSECURE TRANSPORT: BackendUrl is '{backendUrl}'. Credentials, the SIP password and the " +
            "bearer token are sent unencrypted and are readable by anything on the network path. " +
            "Move the backend to https://.");
    }
}
