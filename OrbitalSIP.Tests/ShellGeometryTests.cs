using OrbitalSIP.Models;
using Xunit;

namespace OrbitalSIP.Tests;

/// <summary>
/// Размер и способ размещения окна — свойство поверхности, а не того метода, который
/// её строит. Логин центрируется по экрану, всё остальное держится за нижне-правый
/// угол; сегодня это правило записано только внутри двух методов, которые строят
/// логин, и любой третий путь к нему его теряет.
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
    /// Входящий и мини-звонок — одна и та же полоска. Разъехавшись, они дали бы
    /// анимацию размера на переходе «ответил», которого в модели нет.
    /// </summary>
    [Fact]
    public void IncomingAndCallBarShareTheStripGeometry()
    {
        Assert.Equal(ShellGeometry.For(Shell.Incoming), ShellGeometry.For(Shell.CallBar));
        Assert.Equal(436, ShellGeometry.For(Shell.Incoming).Width);
        Assert.Equal(132, ShellGeometry.For(Shell.Incoming).Height);
    }

    /// <summary>
    /// Ни одна поверхность не остаётся без размера. Ветка по умолчанию, возвращающая
    /// что-нибудь правдоподобное, дала бы новому Shell тихий 320×600 вместо ошибки.
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
