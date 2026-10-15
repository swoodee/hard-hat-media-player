using System;
using System.Windows;
using System.Windows.Media;

namespace VlcRedesign.Controls;

/// <summary>The pixel-art cat from the Figma prototype, shown while nothing is playing.</summary>
public sealed class PixelCat : FrameworkElement
{
    private static readonly string[] Map =
    {
        "..W.........W....",
        "..WG.......GW....",
        "..GGWWWWWWWGG....",
        "KKGGKGGGGGKGGKK..",
        "..GPPGGGGGPPG....",
        "KKGGGGGKGKGGGKK..",
        "..GGGGGGKGGGG....",
        "...GGGGGGGGG.....",
        "....GGGGGGG...W..",
        "....GGGGGGG....W.",
        "...GGGGGGGGG...W.",
        "...GWGGGGGGGG.W..",
        "...GWGGGWGGGGGW..",
        "...GWGGGWGGGGGG..",
        "..WWGWWGWWGGGGG..",
    };

    private static readonly Brush White = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)));
    private static readonly Brush Gray = Freeze(new SolidColorBrush(Color.FromRgb(0xDE, 0xDE, 0xDE)));
    private static readonly Brush Black = Freeze(new SolidColorBrush(Color.FromRgb(0x0E, 0x0E, 0x0E)));
    private static readonly Brush Pink = Freeze(new SolidColorBrush(Color.FromRgb(0xF6, 0xB9, 0xC2)));

    private static Brush Freeze(Brush b) { b.Freeze(); return b; }

    protected override void OnRender(DrawingContext dc)
    {
        int cols = Map[0].Length, rows = Map.Length;
        var cell = Math.Floor(Math.Min(ActualWidth / cols, ActualHeight / rows));
        if (cell < 1) return;
        var ox = Math.Round((ActualWidth - cols * cell) / 2);
        var oy = Math.Round((ActualHeight - rows * cell) / 2);

        for (var y = 0; y < rows; y++)
        for (var x = 0; x < cols; x++)
        {
            var brush = Map[y][x] switch { 'W' => White, 'G' => Gray, 'K' => Black, 'P' => Pink, _ => null };
            if (brush != null)
                dc.DrawRectangle(brush, null, new Rect(ox + x * cell, oy + y * cell, cell, cell));
        }
    }
}
