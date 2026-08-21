namespace OrbitalSIP.Models;

/// <summary>
/// Что показано внутри <see cref="Shell.Panel"/>.
///
/// Отдельный тип, а не расширенный <see cref="NavTab"/>: у меню четыре слота и
/// пятого не будет. На <see cref="Call"/> попадают только через плашку возврата,
/// разворот <see cref="Shell.CallBar"/> или начало звонка — кнопки в меню для него
/// нет, и подсвечивать на нём нечего.
/// </summary>
public enum NavRoute
{
    Dialer,
    Recents,
    Tasks,
    Settings,

    /// <summary>Экран разговора. Допустим только при живом звонке — см. <c>ShellRouter</c>.</summary>
    Call,
}
