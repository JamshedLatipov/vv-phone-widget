using System;
using System.Diagnostics;
using System.Net;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Net;
using SIPSorcery.Media;
using OrbitalSIP.Services.Audio;

namespace OrbitalSIP.Services
{
    public enum CallState { Idle, Ringing, IncomingRinging, Active, OnHold }
    public enum RegistrationState { Unregistered, Registered, Failed, Paused }

    public class SipService : IDisposable
    {
        private readonly object _lock = new();

        /// <summary>SIP activity log. Written from SIPSorcery's callback threads, so it goes through the background writer rather than blocking them on the disk.</summary>
        private readonly Logging.AsyncLogWriter _log;

        private SIPTransport?                _transport;
        private SIPRegistrationUserAgent?    _reg;
        private SIPUserAgent?                _activeCall;
        private SIPServerUserAgent?          _pendingUas;
        private VoIPMediaSession?            _mediaSession;
        private GainAudioEndPoint?           _audioEndPoint;
        private SipSettings                  _settings = new();

        /// <summary>
        /// Backing field for <see cref="State"/>. Volatile because the transition is
        /// written under <see cref="_lock"/> but read all over the place without it —
        /// including by SIPSorcery's own callback threads, which is exactly where the
        /// double-teardown guard in <see cref="OnCallEnded"/> lives. A plain auto-property
        /// gave no guarantee those threads would ever see the Idle write.
        /// </summary>
        private volatile CallState _state = CallState.Idle;

        // ── Public state ──────────────────────────────────────────────
        public RegistrationState RegistrationStatus { get; private set; } = RegistrationState.Unregistered;
        public string    LastRegistrationError { get; private set; } = "";
        public CallState State => _state;
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

            _log = new Logging.AsyncLogWriter(Path.Combine(logDir, "sip.log"));
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

            // Saving from Settings lands here mid-call — the button is reachable from the
            // active-call view. Rebuilding _transport under a live dialog stranded that
            // dialog on a transport about to be disposed: the BYE never went out (the
            // remote side hung until its own RTP timeout), the audio endpoint leaked, and
            // State stayed Active for the rest of the session. End the call first, while
            // the transport that carries it is still up.
            if (State != CallState.Idle)
            {
                Log($"Start requested while State={State}. Ending the current call before rebuilding the stack.");
                Hangup();

                // Hangup's active-call branch returns early and leaves the teardown to the
                // OnCallHungup that SIPSorcery raises from inside ua.Hangup(). Verify
                // rather than trust it: anything still standing here is about to lose its
                // transport regardless, so it has to come down now.
                //
                // Through RollbackToIdle, not an inline teardown: it claims Idle BEFORE
                // CleanupMedia, and CleanupMedia's Close() raises OnRtpClosed synchronously
                // straight back into OnCallEnded. Tearing down while still Active let that
                // re-entry claim the transition first and announce Idle, and this method
                // then announced it a second time.
                if (State != CallState.Idle)
                {
                    Log("Call did not reach Idle from Hangup(). Forcing teardown.");
                    RollbackToIdle();
                }
            }

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
                
                // Diagnose the failure reason
                string diagnosticMessage = DiagnoseRegistrationFailure(reason, settings);
                
                Log($"Registration failed: {uri}, reason={reason}");
                Log($"Diagnostic: {diagnosticMessage}");
                
                LastRegistrationError = diagnosticMessage;
                SetRegistrationStatus(RegistrationState.Failed);
                RegistrationError?.Invoke(LastRegistrationError);
                
                // Notify UI of registration failure with diagnostic info
                HttpErrorNotifier.NotifyException("SipService (Registration)", new Exception($"Registration failed: {diagnosticMessage}"));
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
            // Claim Idle -> Ringing inside the lock. The old code tested State here and
            // assigned it a dozen lines later without any lock, so an INVITE arriving in
            // that window passed its own `State == Idle` test and overwrote _activeCall —
            // two calls sharing one field. Claiming up front means the INVITE handler
            // sees Ringing and declines to set up a second leg.
            SIPTransport transport;
            lock (_lock)
            {
                if (_state != CallState.Idle || _transport == null) return false;
                transport      = _transport;
                _state         = CallState.Ringing;
                ActiveCallerId = destination;
            }
            AnnounceState(CallState.Ringing);

            var ua = new SIPUserAgent(transport, null);
            ua.ClientCallTrying += (_, resp) => Log($"Call trying: {resp.ShortDescription}");
            ua.ClientCallRinging += (_, resp) => Log($"Call ringing: {resp.ShortDescription}");
            ua.ClientCallAnswered += (_, resp) => Log($"Call answered: {resp.ShortDescription}");
            ua.ClientCallFailed += (_, error, resp) => Log($"Call failed: {error}; response={resp?.ShortDescription}");

            if (!TryCreateAudio())
            {
                Log("Outbound call aborted: audio initialisation failed.");
                RollbackToIdle();
                return false;
            }

            // Published before the hangup handlers are wired, never after: OnCallEnded
            // nulls _activeCall, and an assignment sequenced after it would put a dead
            // agent straight back into the field.
            lock (_lock) { _activeCall = ua; }

            // Subscribe BEFORE ua.Call() — the remote can hang up during the
            // INVITE exchange and OnCallHungup fires on SIPSorcery's thread
            // before we would ever reach the if(ok) block below.
            ua.OnCallHungup += _ => { try { OnCallEnded(); } catch (Exception ex) { Log($"OnCallHungup handler threw: {ex}"); } };
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
                // The remote can answer and hang up again inside that await; OnCallEnded
                // has then already claimed Idle, and announcing Active over it would leave
                // the UI in a call that no longer exists.
                lock (_lock)
                {
                    if (_state == CallState.Idle)
                    {
                        Log("CallAsync: the call ended while Call() was still awaiting. Not transitioning to Active.");
                        return false;
                    }
                    _state = CallState.Active;
                    ActiveCallStartedAt = DateTime.Now;
                }

                Debug.WriteLine("[SipService] Call connected — audio active.");
                Log($"Call connected. Remote SDP: {SanitizeSdp(_mediaSession?.RemoteDescription?.ToString())}");
                AnnounceState(CallState.Active);
            }
            else
            {
                Debug.WriteLine("[SipService] Call failed / rejected.");
                Log("Call failed or rejected.");
                RollbackToIdle();
            }
            return ok;
        }

        /// <summary>
        /// Returns to Idle from a call that never came up.
        ///
        /// The Idle write happens before <see cref="CleanupMedia"/> on purpose:
        /// <c>mediaSession.Close()</c> raises OnRtpClosed synchronously on the calling
        /// thread, and that handler routes into <see cref="OnCallEnded"/> — which has to
        /// find Idle already claimed so it bows out instead of announcing a second
        /// transition to a UI that has only just been told about the first.
        /// </summary>
        private void RollbackToIdle()
        {
            SIPServerUserAgent? strandedUas;
            lock (_lock)
            {
                _activeCall = null;
                strandedUas = _pendingUas;
                _pendingUas = null;
                _state      = CallState.Idle;
            }

            // Same debt as in OnCallEnded: an unanswered incoming leg dropped here has
            // nothing left that can answer it. TryRejectPending is a no-op once the leg
            // has been answered or already rejected, so the paths that handled it
            // themselves are unaffected.
            TryRejectPending(strandedUas, "the call was torn down before it was answered");

            CleanupMedia();
            AnnounceState(CallState.Idle);
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

                // Only a BYE naming an ESTABLISHED dialog may end the call; everything else
                // gets the 481 the RFC asks for. Note this deliberately rejects during the
                // ringing window too, where Dialogue is still null — see ByeAuthorization
                // for why "no dialog yet" used to mean "accept anything".
                SIPUserAgent? activeUa;
                lock (_lock) { activeUa = _activeCall; }

                var byeCallId    = req.Header?.CallId;
                var activeCallId = activeUa?.Dialogue?.CallId;

                if (Models.ByeAuthorization.Classify(activeCallId, byeCallId)
                    == Models.ByeDisposition.RejectUnknownDialog)
                {
                    Log($"Ignoring BYE for an unknown dialog. Call-ID={byeCallId}; established={activeCallId ?? "<none>"}.");
                    return _transport != null
                        ? _transport.SendResponseAsync(SIPResponse.GetResponse(
                            req, SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist, null))
                        : Task.CompletedTask;
                }

                OnCallEnded();

                if (_transport != null)
                {
                    var okResponse = SIPResponse.GetResponse(req, SIPResponseStatusCodesEnum.Ok, null);
                    return _transport.SendResponseAsync(okResponse);
                }

                return Task.CompletedTask;
            }

            if (req.Method == SIPMethodsEnum.INVITE)
            {
                // Claim Idle -> IncomingRinging inside the lock, the same way CallAsync
                // claims Idle -> Ringing. Testing State here and assigning it a dozen lines
                // later left the reverse race wide open: this handler read Idle, CallAsync
                // claimed Ringing, and this handler then overwrote _activeCall with the
                // incoming agent — stranding the outbound leg mid-Call(), where Hangup()
                // could no longer reach it and the call hung until its own timeout.
                //
                // Busy still gets no response from here, exactly as before: a re-INVITE for
                // the dialog we are already on (far-end hold) also arrives on this event,
                // and answering it ourselves would break hold. It belongs to SIPSorcery's
                // own agent for that dialog.
                lock (_lock)
                {
                    if (_state != CallState.Idle || _transport == null) return Task.CompletedTask;
                    _state = CallState.IncomingRinging;
                }

                Log($"Incoming INVITE from {remoteEP}. From={req.Header.From?.FromURI}");
                TryMangleInviteRequest(remoteEP, req);

                SIPUserAgent       ua;
                SIPServerUserAgent uas;
                try
                {
                    ua  = new SIPUserAgent(_transport!, null);
                    uas = ua.AcceptCall(req);   // sends 100 Trying
                }
                catch (Exception ex)
                {
                    // The claim above has to be handed back, or the widget sits in
                    // IncomingRinging with no leg behind it and refuses every later call
                    // for the rest of the session.
                    Log($"Accepting the incoming INVITE threw: {ex}");
                    lock (_lock) { _state = CallState.Idle; }
                    return Task.CompletedTask;
                }
                ua.ServerCallCancelled += (_, cancelReq) =>
                {
                    try
                    {
                        Log($"Incoming call cancelled by remote: {cancelReq?.StatusLine}");

                        // Through OnCallEnded, not an inline Idle write. This was the ONE
                        // path to Idle in this class that never reached CleanupMedia: if the
                        // operator had already pressed Answer, TryCreateAudio had built the
                        // audio endpoint and the media session, and a CANCEL landing in that
                        // window (a queue caller giving up exactly as the operator picks up)
                        // leaked one winmm capture handle and one render handle for the life
                        // of the process. Enough of those and waveOutOpen starts refusing —
                        // the operator hears nothing while the PBX records a healthy
                        // two-sided call.
                        //
                        // _pendingUas is cleared first so OnCallEnded finds nothing to
                        // reject: the CANCEL has already terminated that INVITE transaction,
                        // and TryRejectPending would be a second final response to it.
                        lock (_lock) { _pendingUas = null; }
                        OnCallEnded();
                    }
                    catch (Exception ex) { Log($"ServerCallCancelled handler threw: {ex}"); }
                };
                ua.OnCallHungup += dialogue =>
                {
                    try
                    {
                        Log($"Incoming call leg hung up. Call-ID={dialogue?.CallId}");
                        OnCallEnded();
                    }
                    catch (Exception ex) { Log($"OnCallHungup(incoming) handler threw: {ex}"); }
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
                // ua.Hangup() was the old response, and it does nothing for a leg that was
                // never answered: the UAS had already been taken out of _pendingUas, so
                // nothing else could reject it either and the caller heard ringback until
                // their own timeout. Reject it explicitly.
                Log("Answer failed because audio initialisation failed.");
                TryRejectPending(uas, "audio initialisation failed");
                RollbackToIdle();
                return;
            }

            // Subscribe BEFORE ua.Answer() — caller can hang up mid-answer
            // and OnCallHungup fires before we'd reach the line below.
            ua.OnCallHungup += _ => { try { OnCallEnded(); } catch (Exception ex) { Log($"OnCallHungup(answer) handler threw: {ex}"); } };
            ua.OnCallHungup += dialogue => Log($"Answered call hung up. Call-ID={dialogue?.CallId}");

            try
            {
                await ua.Answer(uas, _mediaSession);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SipService] AnswerAsync failed: {ex.Message}");
                Log($"AnswerAsync exception: {ex}");

                // Answer() can throw either side of actually answering, so cover both: a
                // leg it never answered is rejected, one it did is hung up.
                TryRejectPending(uas, "Answer() threw");
                if (uas.IsUASAnswered) { try { ua.Hangup(); } catch (Exception hangupEx) { Log($"Hangup after failed answer threw: {hangupEx.Message}"); } }

                RollbackToIdle();
                return;
            }

            // Guard: caller may have hung up while we were awaiting Answer().
            // OnCallHungup → OnCallEnded() already claimed Idle in that case, and the
            // transition to Active has to be claimed under the same lock to stay ordered
            // against it.
            lock (_lock)
            {
                if (_state == CallState.Idle)
                {
                    Log("AnswerAsync: call was hung up during Answer(). Aborting state transition to Active.");
                    return;
                }
                _state = CallState.Active;
                ActiveCallStartedAt = DateTime.Now;
            }

            AnnounceState(CallState.Active);
            Debug.WriteLine($"[SipService] Answered. Remote SDP:\n{_mediaSession?.RemoteDescription}");
            Log($"Incoming call answered. Remote SDP: {SanitizeSdp(_mediaSession?.RemoteDescription?.ToString())}");
        }

        /// <summary>
        /// Sends the 480 an unanswered incoming leg is owed. Once the UAS has been taken
        /// out of <c>_pendingUas</c> nothing else in this class can reject it, so every
        /// abort path between there and a successful Answer() has to come through here or
        /// the caller is left listening to ringback until their own timeout.
        /// </summary>
        private void TryRejectPending(SIPServerUserAgent? uas, string reason)
        {
            if (uas == null || uas.IsUASAnswered) return;

            try
            {
                uas.Reject(SIPResponseStatusCodesEnum.TemporarilyUnavailable, null, null);
                Log($"Rejected the pending incoming call ({reason}).");
            }
            catch (Exception ex)
            {
                Log($"Rejecting the pending incoming call threw: {ex.Message}");
            }
        }

        public void Decline()
        {
            SIPServerUserAgent? uas;
            lock (_lock) { uas = _pendingUas; _pendingUas = null; }
            uas?.Reject(SIPResponseStatusCodesEnum.BusyHere, null, null);
            Log("Incoming call declined.");
            RollbackToIdle();
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

                    // Taken out of the field BEFORE rejecting, so the RollbackToIdle below
                    // does not find it and send a second final response to the same INVITE.
                    lock (_lock) { if (ReferenceEquals(_pendingUas, uas)) _pendingUas = null; }
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

            RollbackToIdle();
        }

        // ── In-call controls ──────────────────────────────────────────
        public async Task SendDtmfAsync(char digit)
        {
            // Snapshot rather than null-check the field: CleanupMedia nulls _mediaSession
            // from a SIP callback thread, so the old `if (_mediaSession == null) return;`
            // followed by `_mediaSession.SendDtmf(...)` could still dereference null
            // between the two.
            VoIPMediaSession? session;
            lock (_lock) { session = _mediaSession; }
            if (session == null || _state != CallState.Active) return;

            byte code = digit switch
            {
                '1' => 1,  '2' => 2,  '3' => 3,
                '4' => 4,  '5' => 5,  '6' => 6,
                '7' => 7,  '8' => 8,  '9' => 9,
                '0' => 0,  '*' => 10, '#' => 11,
                _   => 255
            };
            if (code != 255)
                await session.SendDtmf(code, CancellationToken.None);
        }

        // Tracks whether we have already called PauseAudio so we never call
        // ResumeAudio while still recording (which throws "Already recording").
        private bool _audioPaused = false;

        private void ApplyAudioState()
        {
            if (_audioEndPoint == null) return;

            try
            {
                if (IsMuted && !_audioPaused)
                {
                    _audioEndPoint.PauseAudio();
                    _audioPaused = true;
                }
                else if (!IsMuted && _audioPaused)
                {
                    _audioEndPoint.ResumeAudio();
                    _audioPaused = false;
                }
            }
            catch (Exception ex)
            {
                // Two failure classes, both must NOT crash the app:
                //  - InvalidOperationException: WaveInEvent "Already/Not recording"
                //    (our _audioPaused tracking drifted) — benign.
                //  - NAudio.MmException "InvalidParameter calling waveInOpen": the
                //    capture device (microphone) can't be opened on this PC. This
                //    previously escaped as an UnhandledException on the mute toggle
                //    and killed the widget. Swallow it — the call stays up (no mic
                //    audio), and the operator can fix the input device without a crash.
                // _audioPaused is deliberately NOT updated here. It used to be set to
                // IsMuted, which recorded the transition as achieved even though it had just
                // thrown: after a failed ResumeAudio the `!IsMuted && _audioPaused` branch
                // could never fire again, so the microphone stayed paused for the rest of the
                // session while the interface showed the operator un-muted. Leaving the flag
                // on its real value means the next toggle retries the operation that failed.
                Log($"ApplyAudioState failed ({ex.GetType().Name}), leaving _audioPaused={_audioPaused}: {ex.Message}");
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
            CallState from, to;
            bool goingOnHold;

            lock (_lock)
            {
                ua = _activeCall;
                if (ua == null || (_state != CallState.Active && _state != CallState.OnHold)) return;

                from        = _state;
                goingOnHold = !IsOnHold;
                to          = goingOnHold ? CallState.OnHold : CallState.Active;

                // Claimed inside the lock. Hold is reachable from the panel, the mini
                // widget and a global hotkey at once, and reading IsOnHold outside it let
                // two toggles both see the pre-toggle value and fire opposing re-INVITEs.
                IsOnHold = goingOnHold;
                _state   = to;
            }

            // The re-INVITE goes out unlocked — it is network I/O, and the SIP callbacks
            // it can wake up contend on this same lock.
            try
            {
                if (goingOnHold) ua.PutOnHold();
                else             ua.TakeOffHold();
            }
            catch (Exception ex)
            {
                Log($"ToggleHold failed ({(goingOnHold ? "PutOnHold" : "TakeOffHold")}): {ex.Message}");
                lock (_lock) { IsOnHold = !goingOnHold; _state = from; }
                AnnounceState(from);
                return;
            }

            Log(goingOnHold ? "Call put on hold." : "Call taken off hold.");
            AnnounceState(to);
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

        /// <summary>Banner for "no speakers", worded the same as the startup probe in AudioDeviceCheck.</summary>
        private static void NotifySpeakerFailure()
        {
            var i18n = I18nService.Instance;
            HttpErrorNotifier.Notify(
                i18n.Get("audio.problemHead", "Проблема со звуком")
                + ": " + i18n.Get("audio.spkFail", "Не удаётся открыть динамики"));
        }

        private bool TryCreateAudio()
        {
            // Built locally and published once, under the lock, at the very end. The fields
            // used to be assigned mid-construction, which left a window where OnCallEnded on
            // a SIPSorcery thread could take _audioEndPoint while _mediaSession was still
            // null — closing the device of the call being set up — or find both null and
            // release nothing while the two references landed a moment later.
            //
            // The flip side is that nothing downstream can find these to release them any
            // more, so the catch below has to do it: that is what keeps this from becoming a
            // third instance of the winmm handle leak.
            GainAudioEndPoint? endPoint = null;
            VoIPMediaSession?  session  = null;

            try
            {
                int outIdx = _settings.AudioOutDeviceIndex;
                int inIdx  = _settings.AudioInDeviceIndex;

                string outName = outIdx < 0 ? "System Default"
                    : (outIdx < WaveOutDevices.Count
                        ? WaveOutDevices.ProductName(outIdx) : $"[{outIdx}]");
                string inName  = inIdx < 0 ? "System Default"
                    : (inIdx < NAudio.Wave.WaveInEvent.DeviceCount
                        ? NAudio.Wave.WaveInEvent.GetCapabilities(inIdx).ProductName : $"[{inIdx}]");

                Debug.WriteLine($"[SipService] Audio OUT: {outName}  IN: {inName}");
                Log($"Audio devices. OUT={outName}; IN={inName}");

                endPoint = new GainAudioEndPoint(
                    new AudioEncoder(),
                    audioOutDeviceIndex: outIdx,
                    audioInDeviceIndex:  inIdx);
                endPoint.OnAudioSourceError += err =>
                {
                    Debug.WriteLine($"[SipService] Audio source error: {err}");
                    Log($"Audio source error: {err}");
                };

                // A render device that fails to open is the one fault nothing else reveals:
                // RTP keeps arriving, the PBX keeps recording both directions, and the operator
                // sits through a silent call with no error anywhere. Surface it.
                endPoint.OnAudioSinkError += err =>
                {
                    Debug.WriteLine($"[SipService] Audio sink error: {err}");
                    Log($"Audio sink error: {err}");
                    NotifySpeakerFailure();
                };

                // The constructor opens playback before anything can subscribe above, so the
                // failure that matters most — the one at call setup — has to be read back.
                if (!endPoint.IsPlaybackDeviceOpen)
                {
                    Log("Playback device is not open after audio init — the operator will hear nothing.");
                    NotifySpeakerFailure();
                }

                endPoint.SourceGain = _settings.MicGainPercent / 100f;
                endPoint.SinkGain   = _settings.SpeakerGainPercent / 100f;
                Log($"Applied gains. mic={_settings.MicGainPercent}% speaker={_settings.SpeakerGainPercent}%");

                // G.722 (wideband HD), PCMU (G.711 μ-law), PCMA (G.711 A-law) —
                // covers every mainstream SIP server and softphone.
                endPoint.RestrictFormats(f =>
                    f.FormatName == "G722" ||
                    f.FormatName == "PCMU" ||
                    f.FormatName == "PCMA");

                // Log exactly which codecs go into the SDP offer.
                var sb = new System.Text.StringBuilder();
                foreach (var fmt in endPoint.GetAudioSinkFormats())
                    sb.Append(fmt.FormatName).Append('/').Append(fmt.ClockRate).Append(' ');
                Debug.WriteLine($"[SipService] Offering codecs: {sb}");
                Log($"Offering codecs: {sb}");

                // Bind RTP to the same local IP that was resolved for SIP (ContactHost).
                // This ensures the SDP answer advertises the correct 'c=' IP so
                // Asterisk sends RTP packets to the right interface.
                IPAddress? rtpBindAddr = null;
                if (!string.IsNullOrEmpty(_transport?.ContactHost))
                    IPAddress.TryParse(_transport.ContactHost, out rtpBindAddr);

                session = new VoIPMediaSession(endPoint.ToMediaEndPoints(),
                    bindAddress: rtpBindAddr)
                {
                    AcceptRtpFromAny = true
                };
                Log($"RTP bind address: {rtpBindAddr?.ToString() ?? "any"}");
                session.OnAudioFormatsNegotiated += formats =>
                    Log($"Negotiated audio formats: {string.Join(", ", formats)}");

                // Count received RTP packets — if this stays 0 the problem is network/NAT,
                // not the audio device.  First packet + every 100th are logged.
                int rtpRxCount = 0;
                session.OnRtpPacketReceived += (ep, _, pkt) =>
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

                session.OnRtpClosed += reason =>
                {
                    try
                    {
                        Debug.WriteLine(
                            $"[SipService] RTP closed: {reason} "
                          + $"({Interlocked.CompareExchange(ref rtpRxCount, 0, 0)} packets received)");
                        Log($"RTP closed: {reason}; packets={Interlocked.CompareExchange(ref rtpRxCount, 0, 0)}");
                        if (State == CallState.Active || State == CallState.Ringing)
                            OnCallEnded();
                    }
                    catch (Exception ex) { Log($"OnRtpClosed handler threw: {ex}"); }
                };
                session.OnTimeout += mediaType =>
                {
                    Debug.WriteLine($"[SipService] RTP TIMEOUT ({mediaType}) — no packets for 30s");
                    Log($"RTP timeout for {mediaType}");
                };
                lock (_lock)
                {
                    _audioEndPoint = endPoint;
                    _mediaSession  = session;
                }

                Debug.WriteLine("[SipService] Audio device opened OK.");
                Log("Audio device opened successfully.");
                return true;
            }
            catch (Exception ex)
            {
                // Nothing else holds these yet — CleanupMedia would find nulls — so release
                // them here. Stop is not release for NAudio: only Dispose issues waveOutClose.
                try { session?.Close("audio setup failed"); } catch (Exception closeEx) { Log($"MediaSession.Close after failed audio setup threw: {closeEx.Message}"); }
                try { endPoint?.Dispose(); }                  catch (Exception dispEx)  { Log($"Audio endpoint dispose after failed audio setup threw: {dispEx.Message}"); }

                Debug.WriteLine($"[SipService] TryCreateAudio failed: {ex.Message}");
                Log($"TryCreateAudio exception: {ex}");
                return false;
            }
        }

        // ── Internals ─────────────────────────────────────────────────
        /// <summary>
        /// Ends the call exactly once, no matter how many callbacks report it.
        ///
        /// OnCallHungup, OnRtpClosed and an incoming BYE all land here, from three
        /// different SIPSorcery threads, for the same hangup. The old guard read State
        /// under the lock and then released it before doing the teardown, so two threads
        /// could both see «not Idle» and both proceed — into a CleanupMedia that closed
        /// the same media session twice. Claiming the transition inside the lock is what
        /// makes the loser return instead.
        /// </summary>
        private void OnCallEnded()
        {
            SIPServerUserAgent? strandedUas;
            lock (_lock)
            {
                if (_state == CallState.Idle) return;
                _state = CallState.Idle;
                _activeCall = null;
                strandedUas = _pendingUas;
                _pendingUas = null;
            }

            // An incoming leg that was still ringing when this fired is owed a final
            // response. Clearing _pendingUas without one left nothing in this class able
            // to send it, and the caller heard ringback until their own timeout.
            TryRejectPending(strandedUas, "the call ended before it was answered");

            Log("Call ended callback fired.");
            CleanupMedia();
            AnnounceState(CallState.Idle);
        }

        /// <summary>
        /// Idempotent: takes the media objects out under the lock and closes its own local
        /// copies, so a second caller finds nulls and closes nothing. Closing is done
        /// outside the lock — CloseAudio drives NAudio device teardown, which is slow and
        /// can throw, and neither belongs under a lock the SIP callbacks contend on.
        /// </summary>
        private void CleanupMedia()
        {
            GainAudioEndPoint? endPoint;
            VoIPMediaSession?  session;
            lock (_lock)
            {
                endPoint       = _audioEndPoint;
                session        = _mediaSession;
                _audioEndPoint = null;
                _mediaSession  = null;

                // Every per-call toggle resets here, not just _audioPaused. IsMuted survived
                // into the next call, and the widget seeds its icon from it, so the operator
                // opened a fresh call already muted with a live-looking microphone — anything
                // said in the first seconds went nowhere. IsOnHold survived the same way.
                _audioPaused   = false;
                IsMuted        = false;
                IsOnHold       = false;
            }

            ActiveCallStartedAt = null;
            if (endPoint == null && session == null) return;

            Log("Cleaning up media resources.");
            try { endPoint?.CloseAudio(); }     catch (Exception ex) { Log($"CloseAudio threw: {ex.Message}"); }
            try { endPoint?.CloseAudioSink(); } catch (Exception ex) { Log($"CloseAudioSink threw: {ex.Message}"); }
            try { session?.Close("ended"); }    catch (Exception ex) { Log($"MediaSession.Close threw: {ex.Message}"); }

            // Closing is not releasing: WaveOutEvent.Stop issues waveOutReset and leaves the
            // winmm handle open, only Dispose issues waveOutClose. Without this the handle
            // from every call stays open for the life of the process, and once waveOutOpen
            // starts refusing them the operator hears nobody — while RTP keeps arriving and
            // the PBX keeps recording a perfectly two-sided call.
            try { endPoint?.Dispose(); }        catch (Exception ex) { Log($"Audio endpoint dispose threw: {ex.Message}"); }
        }

        private void SetState(CallState s)
        {
            _state = s;
            AnnounceState(s);
        }

        /// <summary>
        /// Raises <see cref="CallStateChanged"/> for a transition already written to
        /// <see cref="_state"/>. Split out so a caller that has to claim the transition
        /// atomically (under <see cref="_lock"/>) can still raise the event outside the
        /// lock — subscribers marshal to the UI thread and must never run under it.
        /// </summary>
        private void AnnounceState(CallState s)
        {
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
            _log.Dispose();   // drains the queue, so the shutdown lines reach the file
        }

        private void Log(string message)
        {
            Debug.WriteLine($"[SipService] {message}");
            _log.Write($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}");
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

        /// <summary>
        /// Diagnose SIP registration failures and provide user-friendly error messages.
        /// </summary>
        private static string DiagnoseRegistrationFailure(string? reason, SipSettings settings)
        {
            if (string.IsNullOrWhiteSpace(reason))
                reason = "Unknown error";

            // Common SIP failure reasons and translations
            if (reason.Contains("timeout", StringComparison.OrdinalIgnoreCase) 
                || reason.Contains("request terminated", StringComparison.OrdinalIgnoreCase))
            {
                return $"Server {settings.Server}:{settings.Port} is not responding (timeout). Check if SIP server is running and firewall allows UDP port {settings.Port}.";
            }

            if (reason.Contains("401", StringComparison.OrdinalIgnoreCase) 
                || reason.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
            {
                return $"Authentication failed. Check username '{settings.Username}' and password.";
            }

            if (reason.Contains("403", StringComparison.OrdinalIgnoreCase) 
                || reason.Contains("forbidden", StringComparison.OrdinalIgnoreCase))
            {
                return $"Access denied by server {settings.Server}. Contact administrator.";
            }

            if (reason.Contains("404", StringComparison.OrdinalIgnoreCase) 
                || reason.Contains("not found", StringComparison.OrdinalIgnoreCase))
            {
                return $"User '{settings.Username}' not found on server {settings.Server}.";
            }

            if (reason.Contains("connection refused", StringComparison.OrdinalIgnoreCase) 
                || reason.Contains("unreachable", StringComparison.OrdinalIgnoreCase)
                || reason.Contains("cannot assign", StringComparison.OrdinalIgnoreCase))
            {
                return $"Cannot connect to {settings.Server}:{settings.Port}. Server may be down or network is unreachable.";
            }

            if (reason.Contains("503", StringComparison.OrdinalIgnoreCase) 
                || reason.Contains("service unavailable", StringComparison.OrdinalIgnoreCase))
            {
                return $"SIP server {settings.Server} is temporarily unavailable. Try again later.";
            }

            // Generic fallback
            return $"Registration failed: {reason}";
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
