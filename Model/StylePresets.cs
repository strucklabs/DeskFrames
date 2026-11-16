namespace DeskFrames.Model;

/// <summary>Vorschläge für Rahmenfarben und -breiten (Verwaltungsfenster und Kontextmenü).</summary>
public static class StylePresets
{
    public static readonly IReadOnlyList<(string Name, string Color)> Colors = new[]
    {
        ("Charcoal", FrameConfig.DefaultBorderColor),
        ("White", "#FFFFFF"),
        ("Cream", "#F0E6D2"),
        ("Silver", "#A8A8A8"),
        ("Gold", "#B8964E"),
        ("Walnut", "#6B4A2B"),
        ("Navy", "#1F2A44"),
        ("Forest", "#2F4A3A"),
    };

    public static readonly IReadOnlyList<int> Widths = new[] { 0, 2, 4, 6, 10, 16, 24 };
}
