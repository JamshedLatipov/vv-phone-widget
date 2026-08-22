using OrbitalSIP.Models;
using Xunit;

namespace OrbitalSIP.Tests;

/// <summary>
/// The window's size and placement belong to the surface, not to whichever method builds
/// it. Login centres on the screen and everything else holds the bottom-right corner;
/// today that rule is written down only inside the two methods that build login, and any
/// third way in loses it.
/// </summary>
public class ShellGeometryTests
{
    [Theory]
    [InlineData(Shell.Login)]
    [InlineData(Shell.LoginSettings)]
    public void LoginSurfacesArePanelSizedAndCentered(Shell shell)
    {
        var box = ShellGeometry.For(shell);

        Assert.Equal(320, box.Width);
        Assert.Equal(600, box.Height);
        Assert.Equal(ShellPlacement.CenterOnScreen, box.Placement);
    }

    [Fact]
    public void PanelIsTheSameSizeAsLoginButAnchored()
    {
        var box = ShellGeometry.For(Shell.Panel);

        Assert.Equal(320, box.Width);
        Assert.Equal(600, box.Height);
        Assert.Equal(ShellPlacement.AnchorBottomRight, box.Placement);
    }

    [Fact]
    public void CollapsedIsTheSquareWidget()
    {
        var box = ShellGeometry.For(Shell.Collapsed);

        Assert.Equal(96, box.Width);
        Assert.Equal(96, box.Height);
        Assert.Equal(ShellPlacement.AnchorBottomRight, box.Placement);
    }

    /// <summary>
    /// The incoming call and the in-call strip are the same strip. Letting them drift
    /// apart would animate a resize across the "answered" transition — a resize the model
    /// does not have.
    /// </summary>
    [Fact]
    public void IncomingAndCallBarShareTheStripGeometry()
    {
        Assert.Equal(ShellGeometry.For(Shell.Incoming), ShellGeometry.For(Shell.CallBar));
        Assert.Equal(436, ShellGeometry.For(Shell.Incoming).Width);
        Assert.Equal(132, ShellGeometry.For(Shell.Incoming).Height);
    }

    /// <summary>
    /// No surface is left without a size. A default branch returning something plausible
    /// would hand a newly added Shell a silent 320×600 instead of an error.
    /// </summary>
    [Fact]
    public void EveryShellHasGeometry()
    {
        foreach (Shell shell in Enum.GetValues<Shell>())
        {
            var box = ShellGeometry.For(shell);
            Assert.True(box.Width > 0 && box.Height > 0, $"{shell} has no geometry");
        }
    }
}
