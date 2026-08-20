using System;

namespace OrbitalSIP.Models
{
    /// <summary>What to do with an arriving BYE.</summary>
    public enum ByeDisposition
    {
        /// <summary>The BYE names the dialog we are on. Tear the call down and answer 200.</summary>
        EndActiveCall,

        /// <summary>Not our dialog. Answer 481 and leave the call alone.</summary>
        RejectUnknownDialog
    }

    /// <summary>
    /// Decides whether an arriving BYE may end the operator's call.
    ///
    /// Only a BYE naming an ESTABLISHED dialog qualifies. The previous rule — "reject only
    /// when both Call-IDs are known and differ" — read as a match check but behaved as an
    /// allow-by-default: a <c>SIPUserAgent</c> has no <c>Dialogue</c> until the call is
    /// answered, and SipService publishes the agent before dialling, so for the entire
    /// ringing window there was nothing to compare against and the guard fell through.
    /// Any BYE arriving in that window tore down the call being set up — including the
    /// previous call's BYE still retransmitting on timer F (up to 32 s) after our 200 OK
    /// was lost, which is the non-malicious version of the same thing.
    ///
    /// BYE is defined only inside an established dialog (RFC 3261 §15); a caller who gives
    /// up during setup sends CANCEL, which SIPSorcery routes to ServerCallCancelled instead.
    /// So "no dialog yet" is never a reason to accept one.
    /// </summary>
    public static class ByeAuthorization
    {
        /// <param name="establishedDialogCallId">
        /// Call-ID of the dialog we are actually on, or null/blank when no dialog is
        /// established — no call, or a call still being set up.
        /// </param>
        /// <param name="byeCallId">Call-ID carried by the arriving BYE.</param>
        public static ByeDisposition Classify(string? establishedDialogCallId, string? byeCallId)
        {
            if (string.IsNullOrWhiteSpace(establishedDialogCallId) ||
                string.IsNullOrWhiteSpace(byeCallId))
            {
                return ByeDisposition.RejectUnknownDialog;
            }

            // Ordinal: a Call-ID is an opaque token, so a case-folded match is a different dialog.
            return string.Equals(establishedDialogCallId, byeCallId, StringComparison.Ordinal)
                ? ByeDisposition.EndActiveCall
                : ByeDisposition.RejectUnknownDialog;
        }
    }
}
