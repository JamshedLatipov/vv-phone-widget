using System;
using Material.Icons;

namespace OrbitalSIP.Services;

/// <summary>
/// Picks the glyph for the first tab slot, which carries two affordances: a dial pad
/// and "back to login". It used to carry a third, "back to the call you are on", until
/// the return strip took that job — the slot was pointing at a screen the dialer tab no
/// longer opens.
///
/// The control used to leave this icon wherever the last setter to run had put it, so
/// leaving login mode worked only because MainWindow happened to call SetLoginMode last.
/// The property that matters is that the answer depends on the state and not on the
/// call order, and that property is only worth anything if something pins it.
/// </summary>
public static class NavTabIcon
{
    /// <summary>
    /// Signed out, the slot is a way out of Settings; otherwise it is the dial pad.
    /// </summary>
    public static MaterialIconKind ForDialerTab(bool loginMode) =>
        loginMode ? MaterialIconKind.ArrowLeft
                  : MaterialIconKind.Dialpad;

    /// <summary>
    /// The i18n key whose wording belongs with a glyph.
    ///
    /// Taken from the glyph rather than from the flags a second time, so the tooltip
    /// cannot contradict the arrow it is attached to — which is the whole complaint, and
    /// which restating the precedence here would leave one careless edit away from
    /// returning.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The slot grew a third affordance and this was not told about it. Deliberately not
    /// a default arm: the fallback used to be "Dialer", which would label a brand new glyph
    /// as the dial pad — the same lying tooltip this method exists to prevent, moved one
    /// step along rather than removed.
    /// </exception>
    public static string TooltipKeyFor(MaterialIconKind kind) => kind switch
    {
        MaterialIconKind.ArrowLeft => "Back",
        MaterialIconKind.Dialpad   => "Dialer",
        _ => throw new ArgumentOutOfRangeException(
                 nameof(kind), kind, "No tooltip wording is defined for this dialer-tab glyph."),
    };
}
