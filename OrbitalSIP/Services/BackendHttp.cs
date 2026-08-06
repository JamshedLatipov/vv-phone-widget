using System;
using System.Net.Http;
using System.Net.Security;

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

    private static readonly SocketsHttpHandler Handler = new()
    {
        PooledConnectionLifetime = ConnectionLifetime,
        SslOptions = new SslClientAuthenticationOptions
        {
            // Unchanged from what every one of the nine call sites did on its own.
            // It is wrong, and it is now wrong in exactly one place instead of nine.
            RemoteCertificateValidationCallback = (_, _, _, _) => true
        }
    };

    /// <summary>Shared client for backend calls that are happy with the default timeout.</summary>
    public static HttpClient Client { get; } = new(Handler, disposeHandler: false);

    /// <summary>
    /// A client with its own timeout, over the shared pool. Disposing it releases the
    /// client only — the handler and its sockets stay up for everyone else.
    /// </summary>
    public static HttpClient CreateClient(TimeSpan timeout) =>
        new(Handler, disposeHandler: false) { Timeout = timeout };
}
