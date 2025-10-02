using System;

namespace OpenKh.Tools.ModBrowser.Models;

public class ModBadge : IEquatable<ModBadge>
{
    public ModBadge(string label, string background, string foreground)
    {
        Label = label ?? throw new ArgumentNullException(nameof(label));
        Background = background ?? throw new ArgumentNullException(nameof(background));
        Foreground = foreground ?? throw new ArgumentNullException(nameof(foreground));
    }

    public string Label { get; }

    public string Background { get; }

    public string Foreground { get; }

    public bool Equals(ModBadge? other) =>
        other != null && string.Equals(Label, other.Label, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => obj is ModBadge badge && Equals(badge);

    public override int GetHashCode() =>
        StringComparer.OrdinalIgnoreCase.GetHashCode(Label);

    public static ModBadge CreateLua() => new("Lua", "#2962ff", "White");

    public static ModBadge CreatePc() => new("PC", "#40C4FF", "Black");

    public static ModBadge CreateHd() => new("HD", "#E53935", "White");

    public static ModBadge CreatePs2() => new("PS2", "#B0BEC5", "Black");

    public static ModBadge CreateHeavy() => new("Heavy", "#FB8C00", "Black");

    public static ModBadge CreateKh1() => new("KH1", "#C0C0C0", "Black");

    public static ModBadge CreateKh2() => new("KH2", "#FFD700", "Black");

    public static ModBadge CreateBbs() => new("BBS", "#CD7F32", "White");

    public static ModBadge CreateRecom() => new("Re:CoM", "White", "Black");
}
