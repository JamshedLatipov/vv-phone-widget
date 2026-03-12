using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Net;
using SIPSorcery.Media;
using SIPSorceryMedia.Windows;

namespace OrbitalSIP.Services
{
    public enum CallState { Idle, Ringing, IncomingRinging, Active }

    public class SipService : IDisposable
    {
        private readonly object _lock = new();

        private SIPTransport?                _transport;
        private SIPRegistrationUserAgent?    _reg;
        private SIPUserAgent?                _activeCall;
        private SIPServerUserAgent?          _pendingUas;
        private VoIPMediaSession?            _mediaSession;
        private WindowsAudioEndPoint?        _audioEndPoint;
        private SipSettings                  _settings = new();

        // ── Public state ──────────────────────────────────────────────
        public bool      IsRegistered       { get; private set; }
        public string    LastRegistrationError { get; private set; } = "";
        public CallState State              { get; private set; } = CallState.Idle;
        public string    ActiveCallerId     { get; private set; } = "";

        // ── Events ────────────────────────────────────────────────────
        /// <summary>true = registered, false = unregistered/failed</summary>
        public event Action<bool>?   RegistrationStateChanged;
        /// <summary>Fired when registration fails with the server's reason phrase.</summary>
        public event Action<string>? RegistrationError;
        /// <summary>Fired on the SIPSorcery thread — dispatch to UI before touching controls</summary>
        public event Action<string>? IncomingCallReceived;   // arg = caller ID
        public event Action<CallState>? CallStateChanged;

        // ── Lifecycle ─────────────────────────────────────────────────
        /// <summary>
        /// (Re-)initialise the stack with the supplied settings.
        /// Safe to call multiple times (e.g. after saving new settings).
        /// </summary>
        public void Start(SipSettings settings)
        {
            _settings = settings;

            // Tear down any existing stack
            _reg?.Stop();
            _reg = null;

            if (_transport != null)
                _transport.SIPTransportRequestReceived -= OnSIPRequest;
            _transport?.Shutdown();
            _transport?.Dispose();

            _transport = new SIPTransport();

            if (settings.Transport == "TCP")
                _transport.AddSIPChannel(new SIPTCPChannel(new IPEndPoint(IPAddress.Any, 0)));
            else
                _transport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Any, 0)));

            _transport.SIPTransportRequestReceived += OnSIPRequest;

            if (string.IsNullOrWhiteSpace(settings.Server) ||
                string.IsNullOrWhiteSpace(settings.Username))
            {
                Debug.WriteLine("[SipService] Start skipped — server or username is empty.");
                return;
            }

            // Build the registrar address. Include transport override for non-UDP.
            var serverArg = settings.Transport switch
            {
                "TCP" => $"sip:{settings.Server}:{settings.Port};transport=tcp",
                "TLS" => $"sips:{settings.Server}:{settings.Port}",
                _     => $"{settings.Server}:{settings.Port}"  // UDP default
            };

            Debug.WriteLine($"[SipService] Starting registration: {settings.Username}@{serverArg}");

            _reg = new SIPRegistrationUserAgent(
                _transport, settings.Username, settings.Password,
                serverArg, 120);

            _reg.RegistrationSuccessful += (uri, __) =>
            {
                Debug.WriteLine($"[SipService] Registered: {uri}");
                LastRegistrationError = "";
                IsRegistered = true;
                RegistrationStateChanged?.Invoke(true);
            };
            _reg.RegistrationFailed += (uri, __, reason) =>
            {
                Debug.WriteLine($"[SipService] Registration FAILED: {uri} — {reason}");
                LastRegistrationError = reason ?? "Unknown error";
                IsRegistered = false;
                RegistrationStateChanged?.Invoke(false);
                RegistrationError?.Invoke(LastRegistrationError);
            };
            _reg.Start();
            Debug.WriteLine("[SipService] Registration agent started.");
        }

        // ── Outbound call ─────────────────────────────────────────────
        public async Task<bool> CallAsync(string destination)
        {
            if (State != CallState.Idle || _transport == null) return false;

            var ua = new SIPUserAgent(_transport, null);
            try
            {
                _audioEndPoint = new WindowsAudioEndPoint(new AudioEncoder());
                _mediaSession  = new VoIPMediaSession(_audioEndPoint.ToMediaEndPoints());
            }
            catch
            {
                SetState(CallState.Idle);
                return false;
            }

            _activeCall    = ua;
            ActiveCallerId = destination;
            SetState(CallState.Ringing);

            var dest = destination.Contains('@')
                ? destination
                : $"sip:{destination}@{_settings.Server}";

            bool ok;
            try
            {
                ok = await ua.Call(dest, _settings.Username, _settings.Password, _mediaSession);
            }
            catch
            {
                ok = false;
            }

            if (ok)
            {
                // Subscribe only after the call is confirmed active so hangup
                // events during setup don't race with our cleanup logic.
                ua.OnCallHungup += _ => OnCallEnded();
                SetState(CallState.Active);
            }
            else
            {
                CleanupMedia();
                _activeCall = null;
                SetState(CallState.Idle);
            }
            return ok;
        }

        // ── Incoming call ─────────────────────────────────────────────
        private Task OnSIPRequest(SIPEndPoint localEP, SIPEndPoint remoteEP, SIPRequest req)
        {
            if (req.Method == SIPMethodsEnum.INVITE && State == CallState.Idle)
            {
                var ua  = new SIPUserAgent(_transport!, null);
                var uas = ua.AcceptCall(req);   // sends 100 Trying

                lock (_lock)
                {
                    _activeCall = ua;
                    _pendingUas = uas;
                    ActiveCallerId = req.Header.From?.FromURI?.User ?? "Unknown";
                }

                SetState(CallState.IncomingRinging);
                IncomingCallReceived?.Invoke(ActiveCallerId);
            }
            return Task.CompletedTask;
        }

        public async Task AnswerAsync()
        {
            SIPUserAgent?       ua;
            SIPServerUserAgent? uas;
            lock (_lock)
            {
                ua  = _activeCall;
                uas = _pendingUas;
                _pendingUas = null;
            }
            if (ua == null || uas == null) return;

            try
            {
                _audioEndPoint = new WindowsAudioEndPoint(new AudioEncoder());
                _mediaSession  = new VoIPMediaSession(_audioEndPoint.ToMediaEndPoints());
            }
            catch
            {
                ua.Hangup();
                _activeCall = null;
                SetState(CallState.Idle);
                return;
            }

            try
            {
                await ua.Answer(uas, _mediaSession);
            }
            catch
            {
                CleanupMedia();
                _activeCall = null;
                SetState(CallState.Idle);
                return;
            }

            ua.OnCallHungup += _ => OnCallEnded();
            SetState(CallState.Active);
        }

        public void Decline()
        {
            SIPServerUserAgent? uas;
            lock (_lock) { uas = _pendingUas; _pendingUas = null; _activeCall = null; }
            uas?.Reject(SIPResponseStatusCodesEnum.BusyHere, null, null);
            SetState(CallState.Idle);
        }

        public void Hangup()
        {
            _activeCall?.Hangup();
            CleanupMedia();
            _activeCall = null;
            SetState(CallState.Idle);
        }

        // ── In-call controls ──────────────────────────────────────────
        public async Task SendDtmfAsync(char digit)
        {
            if (_mediaSession == null || State != CallState.Active) return;

            byte code = digit switch
            {
                '1' => 1,  '2' => 2,  '3' => 3,
                '4' => 4,  '5' => 5,  '6' => 6,
                '7' => 7,  '8' => 8,  '9' => 9,
                '0' => 0,  '*' => 10, '#' => 11,
                _   => 255
            };
            if (code != 255)
                await _mediaSession.SendDtmf(code, CancellationToken.None);
        }

        public void SetMuted(bool muted)
        {
            if (muted) _audioEndPoint?.PauseAudio();
            else       _audioEndPoint?.ResumeAudio();
        }

        // ── Internals ─────────────────────────────────────────────────
        private void OnCallEnded()
        {
            CleanupMedia();
            _activeCall = null;
            SetState(CallState.Idle);
        }

        private void CleanupMedia()
        {
            _audioEndPoint?.CloseAudio();
            _mediaSession?.Close("ended");
            _audioEndPoint = null;
            _mediaSession  = null;
        }

        private void SetState(CallState s)
        {
            State = s;
            CallStateChanged?.Invoke(s);
        }

        public void Dispose()
        {
            _reg?.Stop();
            Hangup();
            if (_transport != null)
                _transport.SIPTransportRequestReceived -= OnSIPRequest;
            _transport?.Shutdown();
            _transport?.Dispose();
        }
    }
}
