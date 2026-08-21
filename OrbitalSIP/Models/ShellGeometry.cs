using System;

namespace OrbitalSIP.Models;

/// <summary>Как окно встаёт на экран, когда переходит на поверхность.</summary>
public enum ShellPlacement
{
    /// <summary>Держится за нижне-правый угол — там, где оператор его припарковал.</summary>
    AnchorBottomRight,

    /// <summary>Встаёт по центру рабочей области. Только для экранов входа.</summary>
    CenterOnScreen,
}

/// <summary>Размер поверхности в базовых единицах, до умножения на масштаб виджета.</summary>
public readonly record struct ShellBox(double Width, double Height, ShellPlacement Placement);

/// <summary>
/// Размер и размещение окна как функция от поверхности.
///
/// Константы пришли из MainWindow, где раздавались по девятнадцати вызовам
/// StartAnimation вручную. Масштаб (<c>_uiScale</c>) здесь не применяется намеренно:
/// он свойство экрана и настройки, а не поверхности, и остаётся за окном.
/// </summary>
public static class ShellGeometry
{
    public const double WidgetSize  = 96;
    public const double PanelWidth  = 320;
    public const double PanelHeight = 600;
    public const double StripWidth  = 436;
    public const double StripHeight = 132;

    public static ShellBox For(Shell shell) => shell switch
    {
        Shell.Login or Shell.LoginSettings =>
            new ShellBox(PanelWidth, PanelHeight, ShellPlacement.CenterOnScreen),

        Shell.Panel =>
            new ShellBox(PanelWidth, PanelHeight, ShellPlacement.AnchorBottomRight),

        Shell.Collapsed =>
            new ShellBox(WidgetSize, WidgetSize, ShellPlacement.AnchorBottomRight),

        Shell.Incoming or Shell.CallBar =>
            new ShellBox(StripWidth, StripHeight, ShellPlacement.AnchorBottomRight),

        _ => throw new ArgumentOutOfRangeException(nameof(shell), shell, "Поверхность без геометрии"),
    };
}
