using OrbitalSIP.Models;

namespace OrbitalSIP.Services
{
    /// <summary>What MainWindow does once a transfer attempt has an outcome.</summary>
    public enum TransferOutcomeAction
    {
        /// <summary>The backend moved the call. Tell the operator it worked.</summary>
        NotifySuccess,

        /// <summary>
        /// The backend was never asked — no backend configured, the channel lookup
        /// failed, or a network error/timeout during that read-only lookup. Nothing
        /// was attempted, so a local SIP REFER is safe and is the right recovery.
        /// </summary>
        ReferFallback,

        /// <summary>
        /// The backend answered and refused, or answered ambiguously (a timeout
        /// during the POST, where whether the call already moved is unknown). Report
        /// the failure; a REFER here would either go behind a deliberate refusal or
        /// race a transfer that may already have happened.
        /// </summary>
        NotifyFailure,
    }

    /// <summary>
    /// Decides what MainWindow.RouteTransferAsync does with a transfer's outcome —
    /// split out the way TransferTargetsPresenter splits the picker's list logic out
    /// of TransferDialog: a static, Avalonia-free rule, so every (kind, outcome)
    /// combination can be swept by a test without a window.
    ///
    /// The rule that must never regress: a queue never falls back to REFER. A queue
    /// name is not a dialable number in any dialplan context, so a REFER aimed at one
    /// goes nowhere and leaves the caller on a call the operator thinks they handed
    /// off. ReferFallback is reachable only for TransferTargetKind.Extension.
    /// </summary>
    public static class TransferOutcomePresenter
    {
        /// <summary>
        /// <paramref name="outcome"/> already carries the reasoning — see
        /// <see cref="TransferOutcome"/> and TransferService.TransferAsync's doc
        /// comments for why ChannelUnresolved means "never asked" and Failed means
        /// "asked and refused, or asked and don't know". This only turns that,
        /// plus which kind of target was picked, into what to do next.
        /// </summary>
        public static TransferOutcomeAction SelectAction(TransferTargetKind kind, TransferOutcome outcome) =>
            outcome switch
            {
                TransferOutcome.Ok => TransferOutcomeAction.NotifySuccess,

                // The one arm SIP REFER is safe behind: the backend was never asked,
                // and the target is a real dialable extension. Guarded on kind so a
                // queue with ChannelUnresolved falls through to NotifyFailure below
                // instead — a REFER at a queue name goes nowhere.
                TransferOutcome.ChannelUnresolved when kind == TransferTargetKind.Extension =>
                    TransferOutcomeAction.ReferFallback,

                // Covers Failed (either kind) and ChannelUnresolved for a Queue.
                _ => TransferOutcomeAction.NotifyFailure,
            };
    }
}
