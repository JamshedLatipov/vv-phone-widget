using System;
using System.Threading.Tasks;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;

namespace OrbitalSIP.Services
{
    public class SipService : IDisposable
    {
        private SIPTransport? _sipTransport;
        private SIPRegistrationUserAgent? _regUserAgent;

        public SipService()
        {
        }

        public void Initialize()
        {
            // Initialize SIP transport (placeholder).
            // Detailed channel configuration (UDP/TCP/TLS/WSS) will be added in SIP integration step.
            _sipTransport = new SIPTransport();
        }

        public Task RegisterAsync(string server, string username, string password)
        {
            // Registration implementation will be added during SIP integration.
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _regUserAgent?.Stop();
            _sipTransport?.Shutdown();
        }
    }
}
