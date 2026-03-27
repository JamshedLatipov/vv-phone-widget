using System;
using System.Diagnostics;
using System.Net;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Net;
using SIPSorcery.Media;
using SIPSorceryMedia.Windows;

namespace OrbitalSIP.Services
{
    public enum CallState { Idle, Ringing, IncomingRinging, Active, OnHold }
    public enum RegistrationState { Unregistered, Registered, Failed, Paused }

    public class SipService : IDisposable
    {
        private readonly object _lock = new();
        private readonly object _logLock = new();
        private readonly string _logFilePath;

        private SIPTransport?                _transport;
        private SIPRegistrationUserAgent?    _reg;
        private SIPUserAgent?                _activeCall;
        private SIPServerUserAgent?          _pendingUas;
        private VoIPMediaSession?            _mediaSession;
        private WindowsAudioEndPoint?        _audioEndPoint;
        private SipSettings                  _settings = new();

        // ── Public state ──────────────────────────────────────────────
        public RegistrationState RegistrationStatus { get; private set; } = RegistrationState.Unregistered;
        public string    LastRegistrationError { get; private set; } = "";
        public CallState State              { get; private set; } = CallState.Idle;
        public DateTime? ActiveCallStartedAt  { get; private set; }
        public string    ActiveCallerId     { get; private set; } = "";
        public SipSettings CurrentSettings => _settings;

        // ── Events ────────────────────────────────────────────────────
        public event Action<RegistrationState>? RegistrationStatusChanged;
        /// <summary>Fired when registration fails with the server's reason phrase.</summary>
        public event Action<string>? RegistrationError;
        /// <summary>Fired on the SIPSorcery thread — dispatch to UI before touching controls</summary>
        public event Action<string>? IncomingCallReceived;   // arg = caller ID
        public event Action<CallState>? CallStateChanged;

        public SipService()
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OrbitalSIP",
                "logs");

            Directory.CreateDirectory(logDir);
            _logFilePath = Path.Combine(logDir, "sip.log");
            Log("SipService initialised.");
        }

        // ── Lifecycle ─────────────────────────────────────────────────
        /// <summary>
        /// (Re-)initialise the stack with the supplied settings.
        /// Safe to call multiple times (e.g. after saving new settings).
        /// </summary>
        public void Start(SipSettings settings)
        {
            _settings = settings;
            Log($"Start requested. Server={settings.Server}, Port={settings.Port}, User={settings.Username}, Transport={settings.Transport}.");

            // Set to unregistered until we actually start the agent
            SetRegistrationStatus(RegistrationState.Unregistered);

            // Tear down any existing stack
            _reg?.Stop();
            _reg = null;

            if (_transport != null)
                _transport.SIPTransportRequestReceived -= OnSIPRequest;
            _transport?.Shutdown();
            _transport?.Dispose();

            _transport = new SIPTransport();
            _transport.EnableTraceLogs();
            if (IsLocalServerConfigured())
            {
                _transport.ContactHost = IPAddress.Loopback.ToString();
                Log("Using loopback ContactHost for local SIP server.");
            }
            _transport.SIPRequestInTraceEvent += (localEP, remoteEP, req) =>
                Log($"SIP IN REQ {remoteEP} -> {localEP}: {req.StatusLine}");
            _transport.SIPRequestOutTraceEvent += (localEP, remoteEP, req) =>
                Log($"SIP OUT REQ {localEP} -> {remoteEP}: {req.StatusLine}");
            _transport.SIPResponseInTraceEvent += (localEP, remoteEP, resp) =>
            {
                TryMangleInviteResponse(remoteEP, resp);
                Log($"SIP IN RESP {remoteEP} -> {localEP}: {resp.ShortDescription}");
            };
            _transport.SIPResponseOutTraceEvent += (localEP, remoteEP, resp) =>
                Log($"SIP OUT RESP {localEP} -> {remoteEP}: {resp.ShortDescription}");
            _transport.SIPRequestRetransmitTraceEvent += (tx, req, count) =>
                Log($"SIP REQ RETRANSMIT #{count}: {req.StatusLine}");
            _transport.SIPResponseRetransmitTraceEvent += (tx, resp, count) =>
                Log($"SIP RESP RETRANSMIT #{count}: {resp.ShortDescription}");

            var bindAddress = IsLocalServerConfigured() ? IPAddress.Loopback : IPAddress.Any;
            if (settings.Transport == "TCP")
                _transport.AddSIPChannel(new SIPTCPChannel(new IPEndPoint(bindAddress, 0)));
            else
                _transport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(bindAddress, 0)));

            // Explicitly set ContactHost to the local IP that routes towards the SIP server,
            // so Asterisk sends INVITEs to the correct interface.
            if (!IsLocalServerConfigured() && IPAddress.TryParse(settings.Server, out var serverIp))
            {
                try
                {
                    using var probe = new System.Net.Sockets.UdpClient();
                    probe.Connect(serverIp, int.TryParse(settings.Port, out var p) ? p : 5060);
                    _transport.ContactHost = ((IPEndPoint)probe.Client.LocalEndPoint!).Address.ToString();
                    Log($"ContactHost resolved to {_transport.ContactHost}.");
                }
                catch { /* fall through – SIPSorcery will pick one */ }
            }

            _transport.SIPTransportRequestReceived += OnSIPRequest;

            if (!string.IsNullOrWhiteSpace(settings.Server) && !string.IsNullOrWhiteSpace(settings.Username))
            {

            // Build the registrar address. Include transport override for non-UDP.
            var serverArg = settings.Transport switch
            {
                "TCP" => $"sip:{settings.Server}:{settings.Port};transport=tcp",
                "TLS" => $"sips:{settings.Server}:{settings.Port}",
                _     => $"{settings.Server}:{settings.Port}"  // UDP default
            };

            Debug.WriteLine($"[SipService] Starting registration: {settings.Username}@{serverArg}");
            Log($"Starting registration using {serverArg}.");

            _reg = new SIPRegistrationUserAgent(
                _transport, settings.Username, settings.Password,
                serverArg, 120);

            _reg.RegistrationSuccessful += (uri, __) =>
            {
                Debug.WriteLine($"[SipService] Registered: {uri}");
                Log($"Registration successful: {uri}");
                LastRegistrationError = "";
                SetRegistrationStatus(RegistrationState.Registered);
            };
            _reg.RegistrationFailed += (uri, __, reason) =>
            {
                Debug.WriteLine($"[SipService] Registration FAILED: {uri} — {reason}");
                Log($"Registration failed: {uri}, reason={reason}");
                LastRegistrationError = reason ?? "Unknown error";
                SetRegistrationStatus(RegistrationState.Failed);
                RegistrationError?.Invoke(LastRegistrationError);
            };
            _reg.Start();
            }
            Debug.WriteLine("[SipService] Registration agent started.");
        }

        private void SetRegistrationStatus(RegistrationState status)
        {
            RegistrationStatus = status;
            RegistrationStatusChanged?.Invoke(status);
        }

        // ── Outbound call ─────────────────────────────────────────────
        public async Task<bool> CallAsync(string destination)
        {
            if (State != CallState.Idle || _transport == null) return false;

            var ua = new SIPUserAgent(_transport, null);
            ua.ClientCallTrying += (_, resp) => Log($"Call trying: {resp.ShortDescription}");
            ua.ClientCallRinging += (_, resp) => Log($"Call ringing: {resp.ShortDescription}");
            ua.ClientCallAnswered += (_, resp) => Log($"Call answered: {resp.ShortDescription}");
            ua.ClientCallFailed += (_, error, resp) => Log($"Call failed: {error}; response={resp?.ShortDescription}");
            if (!TryCreateAudio()) return false;

            _activeCall    = ua;
            ActiveCallerId = destination;
            SetState(CallState.Ringing);

            // Subscribe BEFORE ua.Call() — the remote can hang up during the
            // INVITE exchange and OnCallHungup fires on SIPSorcery's thread
            // before we would ever reach the if(ok) block below.
            ua.OnCallHungup += _ => OnCallEnded();
            ua.OnCallHungup += dialogue => Log($"Call hung up. Call-ID={dialogue?.CallId}");

            var dest = destination.Contains('@')
                ? destination
                : $"sip:{destination}@{_settings.Server}";

            Debug.WriteLine($"[SipService] Calling: {dest}");
            Log($"Calling destination {dest}");

            bool ok;
            try
            {
                ok = await ua.Call(dest, _settings.Username, _settings.Password, _mediaSession);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SipService] CallAsync failed: {ex.Message}");
                Log($"CallAsync exception: {ex}");
                ok = false;
            }

            if (ok)
            {
                Debug.WriteLine("[SipService] Call connected — audio active.");
                Debug.WriteLine($"[SipService] Remote SDP:\n{_mediaSession?.RemoteDescription}");
                Log($"Call connected. Remote SDP: {SanitizeSdp(_mediaSession?.RemoteDescription?.ToString())}");
                ActiveCallStartedAt = DateTime.Now;
                SetState(CallState.Active);
            }
            else
            {
                Debug.WriteLine("[SipService] Call failed / rejected.");
                Log("Call failed or rejected.");
                // Set Idle BEFORE CleanupMedia so that the OnRtpClosed callback
                // (which fires synchronously inside mediaSession.Close) sees
                // State == Idle and the OnCallEnded guard skips the duplicate transition.
                _activeCall = null;
                SetState(CallState.Idle);
                CleanupMedia();
            }
            return ok;
        }

        // ── Incoming call ─────────────────────────────────────────────
        private Task OnSIPRequest(SIPEndPoint localEP, SIPEndPoint remoteEP, SIPRequest req)
        {
            // Respond to OPTIONS (Asterisk qualify/keepalive) so the endpoint stays reachable.
            if (req.Method == SIPMethodsEnum.OPTIONS)
            {
                Log($"Incoming OPTIONS from {remoteEP}, replying 200 OK.");
                if (_transport != null)
                {
                    var okResp = SIPResponse.GetResponse(req, SIPResponseStatusCodesEnum.Ok, null);
                    return _transport.SendResponseAsync(okResp);
                }
                return Task.CompletedTask;
            }

            if (req.Method == SIPMethodsEnum.BYE)
            {
                Log($"Incoming BYE from {remoteEP}. Request-URI={req.URI}");
                OnCallEnded();

                if (_transport != null)
                {
                    var okResponse = SIPResponse.GetResponse(req, SIPResponseStatusCodesEnum.Ok, null);
                    return _transport.SendResponseAsync(okResponse);
                }

                return Task.CompletedTask;
            }

            if (req.Method == SIPMethodsEnum.INVITE && State == CallState.Idle)
            {
                Log($"Incoming INVITE from {remoteEP}. From={req.Header.From?.FromURI}");
                TryMangleInviteRequest(remoteEP, req);
                var ua  = new SIPUserAgent(_transport!, null);
                var uas = ua.AcceptCall(req);   // sends 100 Trying
                ua.ServerCallCancelled += (_, cancelReq) =>
                {
                    Log($"Incoming call cancelled by remote: {cancelReq?.StatusLine}");
                    lock (_lock) { _pendingUas = null; _activeCall = null; }
                    SetState(CallState.Idle);
                };
                ua.OnCallHungup += dialogue =>
                {
                    Log($"Incoming call leg hung up. Call-ID={dialogue?.CallId}");
                    OnCallEnded();
                };

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

            if (!TryCreateAudio())
            {
                ua.Hangup();
                Log("Answer failed because audio initialisation failed.");
                _activeCall = null;
                SetState(CallState.Idle);
                return;
            }

            // Subscribe BEFORE ua.Answer() — caller can hang up mid-answer
            // and OnCallHungup fires before we'd reach the line below.
            ua.OnCallHungup += _ => OnCallEnded();
            ua.OnCallHungup += dialogue => Log($"Answered call hung up. Call-ID={dialogue?.CallId}");

            try
            {
                await ua.Answer(uas, _mediaSession);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SipService] AnswerAsync failed: {ex.Message}");
                Log($"AnswerAsync exception: {ex}");
                CleanupMedia();
                _activeCall = null;
                SetState(CallState.Idle);
                return;
            }

            ActiveCallStartedAt = DateTime.Now;
            SetState(CallState.Active);
            Debug.WriteLine($"[SipService] Answered. Remote SDP:\n{_mediaSession?.RemoteDescription}");
            Log($"Incoming call answered. Remote SDP: {SanitizeSdp(_mediaSession?.RemoteDescription?.ToString())}");
        }

        public void Decline()
        {
            SIPServerUserAgent? uas;
            lock (_lock) { uas = _pendingUas; _pendingUas = null; _activeCall = null; }
            uas?.Reject(SIPResponseStatusCodesEnum.BusyHere, null, null);
            Log("Incoming call declined.");
            SetState(CallState.Idle);
        }

        public void Hangup()
        {
            SIPUserAgent? ua;
            SIPServerUserAgent? uas;
            lock (_lock)
            {
                ua = _activeCall;
                uas = _pendingUas;
            }

            try
            {
                if (ua?.IsCallActive == true)
                {
                    Debug.WriteLine("[SipService] Hanging up active call.");
                    Log("Hangup requested for active call.");
                    ua.Hangup();
                    return;
                }
                else if (ua != null && (ua.IsCalling || ua.IsRinging))
                {
                    Debug.WriteLine("[SipService] Cancelling outbound call.");
                    Log("Hangup requested during outbound setup. Sending CANCEL.");
                    ua.Cancel();
                }
                else if (uas != null && !uas.IsUASAnswered)
                {
                    Debug.WriteLine("[SipService] Rejecting pending incoming call from hangup action.");
                    Log("Hangup requested for pending incoming call. Sending reject.");
                    uas.Reject(SIPResponseStatusCodesEnum.BusyHere, null, null);
                }
                else
                {
                    Debug.WriteLine($"[SipService] Hangup requested but no SIP leg matched. State={State}.");
                    Log($"Hangup requested but no SIP leg matched. State={State}.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SipService] Hangup failed: {ex.Message}");
                Log($"Hangup exception: {ex}");
            }

            CleanupMedia();
            lock (_lock)
            {
                _pendingUas = null;
                _activeCall = null;
            }
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

        private void ApplyAudioState()
        {
            if (IsMuted)
            {
                _audioEndPoint?.PauseAudio();
            }
            else
            {
                _audioEndPoint?.ResumeAudio();
            }
        }

        public bool IsMuted { get; private set; }

        public void SetMuted(bool muted)
        {
            IsMuted = muted;
            ApplyAudioState();
        }

        public bool IsOnHold { get; private set; }

        public void ToggleHold()
        {
            SIPUserAgent? ua;
            lock (_lock) { ua = _activeCall; }
            if (ua == null || (State != CallState.Active && State != CallState.OnHold)) return;

            if (IsOnHold)
            {
                ua.TakeOffHold();
                IsOnHold = false;
                SetState(CallState.Active);
                Log("Call taken off hold.");
            }
            else
            {
                ua.PutOnHold();
                IsOnHold = true;
                SetState(CallState.OnHold);
                Log("Call put on hold.");
            }
        }

        public async Task<bool> BlindTransferAsync(string destination)
        {
            SIPUserAgent? ua;
            lock (_lock) { ua = _activeCall; }
            if (ua == null || State != CallState.Active) return false;

            var dest = destination.Contains('@')
                ? destination
                : $"sip:{destination}@{_settings.Server}";

            var destUri = SIPURI.ParseSIPURIRelaxed(dest);
            if (destUri == null)
            {
                Log($"BlindTransfer: could not parse destination '{destination}'.");
                return false;
            }

            Log($"Blind transfer to {destUri}.");
            try
            {
                return await ua.BlindTransfer(destUri, TimeSpan.FromSeconds(5), CancellationToken.None);
            }
            catch (Exception ex)
            {
                Log($"BlindTransfer exception: {ex}");
                return false;
            }
        }

        // ── Audio helpers ─────────────────────────────────────────────
        private bool TryCreateAudio()
        {
            try
            {
                int outIdx = _settings.AudioOutDeviceIndex;
                int inIdx  = _settings.AudioInDeviceIndex;

                string outName = outIdx < 0 ? "System Default"
                    : (outIdx < NAudio.Wave.WaveOut.DeviceCount
                        ? NAudio.Wave.WaveOut.GetCapabilities(outIdx).ProductName : $"[{outIdx}]");
                string inName  = inIdx < 0 ? "System Default"
                    : (inIdx < NAudio.Wave.WaveIn.DeviceCount
                        ? NAudio.Wave.WaveIn.GetCapabilities(inIdx).ProductName : $"[{inIdx}]");

                Debug.WriteLine($"[SipService] Audio OUT: {outName}  IN: {inName}");
                Log($"Audio devices. OUT={outName}; IN={inName}");

                _audioEndPoint = new WindowsAudioEndPoint(
                    new AudioEncoder(),
                    audioOutDeviceIndex: outIdx,
                    audioInDeviceIndex:  inIdx);
                _audioEndPoint.OnAudioSourceError += err =>
                {
                    Debug.WriteLine($"[SipService] Audio source error: {err}");
                    Log($"Audio source error: {err}");
                };

                // G.722 (wideband HD), PCMU (G.711 μ-law), PCMA (G.711 A-law) —
                // covers every mainstream SIP server and softphone.
                _audioEndPoint.RestrictFormats(f =>
                    f.FormatName == "G722" ||
                    f.FormatName == "PCMU" ||
                    f.FormatName == "PCMA");

                // Log exactly which codecs go into the SDP offer.
                var sb = new System.Text.StringBuilder();
                foreach (var fmt in _audioEndPoint.GetAudioSinkFormats())
                    sb.Append(fmt.FormatName).Append('/').Append(fmt.ClockRate).Append(' ');
                Debug.WriteLine($"[SipService] Offering codecs: {sb}");
                Log($"Offering codecs: {sb}");

                // Bind RTP to the same local IP that was resolved for SIP (ContactHost).
                // This ensures the SDP answer advertises the correct 'c=' IP so
                // Asterisk sends RTP packets to the right interface.
                IPAddress? rtpBindAddr = null;
                if (!string.IsNullOrEmpty(_transport?.ContactHost))
                    IPAddress.TryParse(_transport.ContactHost, out rtpBindAddr);

                _mediaSession = new VoIPMediaSession(_audioEndPoint.ToMediaEndPoints(),
                    bindAddress: rtpBindAddr)
                {
                    AcceptRtpFromAny = true
                };
                Log($"RTP bind address: {rtpBindAddr?.ToString() ?? "any"}");
                _mediaSession.OnAudioFormatsNegotiated += formats =>
                    Log($"Negotiated audio formats: {string.Join(", ", formats)}");

                // Count received RTP packets — if this stays 0 the problem is network/NAT,
                // not the audio device.  First packet + every 100th are logged.
                int rtpRxCount = 0;
                _mediaSession.OnRtpPacketReceived += (ep, _, pkt) =>
                {
                    int n = Interlocked.Increment(ref rtpRxCount);
                    if (n == 1 || n % 100 == 0)
                    {
                        Debug.WriteLine(
                            $"[SipService] RTP rx #{n}: pt={pkt.Header.PayloadType} "
                          + $"seq={pkt.Header.SequenceNumber} from {ep}");
                        Log($"RTP rx #{n}: pt={pkt.Header.PayloadType} seq={pkt.Header.SequenceNumber} from {ep}");
                    }
                };

                _mediaSession.OnRtpClosed += reason =>
                {
                    Debug.WriteLine(
                        $"[SipService] RTP closed: {reason} "
                      + $"({Interlocked.CompareExchange(ref rtpRxCount, 0, 0)} packets received)");
                    Log($"RTP closed: {reason}; packets={Interlocked.CompareExchange(ref rtpRxCount, 0, 0)}");
                    if (State == CallState.Active || State == CallState.Ringing)
                        OnCallEnded();
                };
                _mediaSession.OnTimeout += mediaType =>
                {
                    Debug.WriteLine($"[SipService] RTP TIMEOUT ({mediaType}) — no packets for 30s");
                    Log($"RTP timeout for {mediaType}");
                    if (State == CallState.Active || State == CallState.Ringing)
                        OnCallEnded();
                };
                Debug.WriteLine("[SipService] Audio device opened OK.");
                Log("Audio device opened successfully.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SipService] TryCreateAudio failed: {ex.Message}");
                Log($"TryCreateAudio exception: {ex}");
                return false;
            }
        }

        // ── Internals ─────────────────────────────────────────────────
        private void OnCallEnded()
        {
            // Guard against double-fire (OnCallHungup + OnRtpClosed can both arrive).
            lock (_lock)
            {
                if (State == CallState.Idle) return;
            }
            Log("Call ended callback fired.");
            CleanupMedia();
            _activeCall = null;
            SetState(CallState.Idle);
        }

        private void CleanupMedia()
        {
            ActiveCallStartedAt = null;
            Log("Cleaning up media resources.");
            _audioEndPoint?.CloseAudio();
            _mediaSession?.Close("ended");
            _audioEndPoint = null;
            _mediaSession  = null;
        }

        private void SetState(CallState s)
        {
            State = s;
            Log($"Call state changed to {s}.");
            CallStateChanged?.Invoke(s);
        }

        public void Dispose()
        {
            Log("SipService disposing.");
            _reg?.Stop();
            Hangup();
            if (_transport != null)
                _transport.SIPTransportRequestReceived -= OnSIPRequest;
            _transport?.Shutdown();
            _transport?.Dispose();
        }

        private void Log(string message)
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}";
            Debug.WriteLine($"[SipService] {message}");

            lock (_logLock)
            {
                File.AppendAllText(_logFilePath, line + Environment.NewLine, Encoding.UTF8);
            }
        }

        private bool IsLocalServerConfigured()
        {
            if (string.IsNullOrWhiteSpace(_settings.Server))
            {
                return false;
            }

            var host = _settings.Server.Trim();
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
        }

        private static bool IsPrivateAddress(IPAddress address)
        {
            if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                return false;
            }

            var bytes = address.GetAddressBytes();
            return bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 168);
        }

        private void TryMangleInviteRequest(SIPEndPoint remoteEP, SIPRequest req)
        {
            if (remoteEP == null || req == null || string.IsNullOrWhiteSpace(req.Body))
                return;

            try
            {
                var sdp = SDP.ParseSDPDescription(req.Body);
                var addr = sdp?.Connection?.ConnectionAddress;
                if (string.IsNullOrWhiteSpace(addr) && sdp?.Media != null && sdp.Media.Count > 0)
                    addr = sdp.Media[0].Connection?.ConnectionAddress;

                if (!IPAddress.TryParse(addr, out var bodyIp)
                    || IPAddress.Equals(bodyIp, remoteEP.Address)
                    || !IsPrivateAddress(bodyIp))
                    return;

                var originalSdp = SanitizeSdp(req.Body);
                req.Body = SIPPacketMangler.MangleSDP(req.Body, remoteEP.Address.ToString(), out _);
                Log($"Mangled incoming INVITE SDP. RemoteEP={remoteEP}; {originalSdp} -> {SanitizeSdp(req.Body)}");
            }
            catch (Exception ex)
            {
                Log($"Failed to mangle incoming INVITE SDP: {ex.Message}");
            }
        }

        private void TryMangleInviteResponse(SIPEndPoint remoteEP, SIPResponse resp)
        {
            if (remoteEP == null || resp == null || resp.Header == null)
            {
                return;
            }

            if (resp.Header.CSeqMethod != SIPMethodsEnum.INVITE)
            {
                return;
            }

            var hadPrivateContact = resp.Header.Contact != null
                && resp.Header.Contact.Count > 0
                && IPAddress.TryParse(resp.Header.Contact[0].ContactURI?.HostAddress, out var contactAddress)
                && !IPAddress.Equals(contactAddress, remoteEP.Address)
                && IsPrivateAddress(contactAddress);

            var hadPrivateSdp = false;
            if (!string.IsNullOrWhiteSpace(resp.Body))
            {
                try
                {
                    var parsedSdp = SDP.ParseSDPDescription(resp.Body);
                    var sdpAddress = parsedSdp?.Connection?.ConnectionAddress;
                    if (string.IsNullOrWhiteSpace(sdpAddress) && parsedSdp?.Media != null && parsedSdp.Media.Count > 0)
                    {
                        sdpAddress = parsedSdp.Media[0].Connection?.ConnectionAddress;
                    }

                    if (IPAddress.TryParse(sdpAddress, out var bodyAddress)
                        && !IPAddress.Equals(bodyAddress, remoteEP.Address)
                        && IsPrivateAddress(bodyAddress))
                    {
                        hadPrivateSdp = true;
                    }
                }
                catch (Exception ex)
                {
                    Log($"Failed to inspect SDP before mangling: {ex.Message}");
                }
            }

            if (!hadPrivateContact && !hadPrivateSdp)
            {
                return;
            }

            var originalContact = resp.Header.Contact != null && resp.Header.Contact.Count > 0
                ? resp.Header.Contact[0].ContactURI?.ToString()
                : "<none>";
            var originalSdp = SanitizeSdp(resp.Body);

            SIPPacketMangler.MangleSIPResponse(resp, remoteEP);
            if (resp.Header.Contact != null && resp.Header.Contact.Count > 0)
            {
                var originalUri = resp.Header.Contact[0].ContactURI;
                resp.Header.Contact[0].ContactURI = new SIPURI(originalUri?.User, remoteEP.GetIPEndPoint().ToString(), null, originalUri?.Scheme ?? SIPSchemesEnum.sip, remoteEP.Protocol);
            }
            Log($"Mangled INVITE response. RemoteEP={remoteEP}; Contact {originalContact} -> {resp.Header.Contact?[0].ContactURI}; SDP {originalSdp} -> {SanitizeSdp(resp.Body)}");
        }

        private static string SanitizeSdp(string? sdp)
        {
            if (string.IsNullOrWhiteSpace(sdp))
            {
                return "<empty>";
            }

            return sdp.Replace("\r", " ").Replace("\n", " | ");
        }
    }
}
