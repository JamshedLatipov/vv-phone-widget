using OrbitalSIP.Models;
using Xunit;

namespace OrbitalSIP.Tests;

/// <summary>
/// Who is allowed to end the operator's call with a BYE.
///
/// SIP runs over UDP here, so a BYE is trivially spoofable and, more mundanely,
/// retransmits: if our 200 OK to the last call's BYE is lost, the far end keeps
/// resending that BYE for up to 32 seconds (timer F) — straight into whatever call
/// the operator has started since.
/// </summary>
public class ByeAuthorizationTests
{
    private const string OurDialog   = "call-id-of-the-call-we-are-on";
    private const string OtherDialog = "call-id-of-some-other-call";

    [Fact]
    public void EndsTheCallWhenTheByeNamesOurEstablishedDialog()
    {
        Assert.Equal(
            ByeDisposition.EndActiveCall,
            ByeAuthorization.Classify(establishedDialogCallId: OurDialog, byeCallId: OurDialog));
    }

    [Fact]
    public void RejectsAByeForADifferentDialog()
    {
        Assert.Equal(
            ByeDisposition.RejectUnknownDialog,
            ByeAuthorization.Classify(establishedDialogCallId: OurDialog, byeCallId: OtherDialog));
    }

    /// <summary>
    /// The regression this exists for. A SIPUserAgent has no Dialogue until the call is
    /// answered, and SipService publishes the agent before dialling — so for the whole
    /// ringing window there was nothing to compare against, the guard fell through, and
    /// ANY arriving BYE tore down the call being set up.
    /// </summary>
    [Fact]
    public void RejectsAByeWhileTheOutgoingCallIsStillRinging()
    {
        Assert.Equal(
            ByeDisposition.RejectUnknownDialog,
            ByeAuthorization.Classify(establishedDialogCallId: null, byeCallId: OtherDialog));
    }

    /// <summary>
    /// Same window, but the BYE happens to carry the Call-ID we will eventually use.
    /// Still rejected: BYE is only defined for an established dialog (RFC 3261 §15),
    /// and a caller giving up mid-setup sends CANCEL, not BYE.
    /// </summary>
    [Fact]
    public void RejectsAByeBeforeADialogExistsEvenWhenTheCallIdWouldMatchLater()
    {
        Assert.Equal(
            ByeDisposition.RejectUnknownDialog,
            ByeAuthorization.Classify(establishedDialogCallId: null, byeCallId: OurDialog));
    }

    [Fact]
    public void RejectsAByeThatCarriesNoCallId()
    {
        Assert.Equal(
            ByeDisposition.RejectUnknownDialog,
            ByeAuthorization.Classify(establishedDialogCallId: OurDialog, byeCallId: null));
    }

    [Fact]
    public void RejectsAByeWhenThereIsNoCallAtAll()
    {
        Assert.Equal(
            ByeDisposition.RejectUnknownDialog,
            ByeAuthorization.Classify(establishedDialogCallId: null, byeCallId: null));
    }

    /// <summary>Call-IDs are case-sensitive tokens; a case-folded match is a different dialog.</summary>
    [Fact]
    public void ComparesCallIdsCaseSensitively()
    {
        Assert.Equal(
            ByeDisposition.RejectUnknownDialog,
            ByeAuthorization.Classify(establishedDialogCallId: OurDialog, byeCallId: OurDialog.ToUpperInvariant()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TreatsABlankEstablishedDialogAsNoDialog(string blank)
    {
        Assert.Equal(
            ByeDisposition.RejectUnknownDialog,
            ByeAuthorization.Classify(establishedDialogCallId: blank, byeCallId: OurDialog));
    }
}
