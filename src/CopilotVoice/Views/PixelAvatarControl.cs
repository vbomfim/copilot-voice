using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CopilotVoice.Views;

/// <summary>
/// Custom Avalonia control that renders the Copilot CLI robot as colored ASCII art.
/// </summary>
public class PixelAvatarControl : Control
{
    private string[]? _frame;
    private double _fontSize = 14;
    private readonly Typeface _typeface = new("Cascadia Mono, Menlo, Consolas, monospace");

    // Colors from the Copilot CLI robot
    private static readonly SolidColorBrush Purple = new(Color.Parse("#C78CE1"));
    private static readonly SolidColorBrush Cyan = new(Color.Parse("#9BDCDF"));
    private static readonly SolidColorBrush Green = new(Color.Parse("#8ABC81"));
    private static readonly SolidColorBrush Dark = new(Color.Parse("#CDD6F4")); // default/face

    // Per-character color map: P=purple, C=cyan, G=green, .=default
    private static readonly string[] ColorMap = [
        ".......PPPPPPPP.......",  // row 0: dome purple
        "...CCCCCCCCCCCCCCCC...",  // row 1: goggles cyan
        "..CC......CC......CC..",  // row 2: goggles cyan frame
        "..CCC....CCCC....CCC..",  // row 3: goggles cyan frame
        ".PPPCCCCCCCCCCCCCCPPP.",  // row 4: bridge cyan, edges purple
        "PPPP.....G..G.....PPPP",  // row 5: cheeks purple, eyes green
        "PPPP.....G..G.....PPPP",  // row 6: cheeks purple, eyes green
        "PPPPP............PPPPP",  // row 7: cheeks purple
        "...PPPPPPPPPPPPPPPP...",  // row 8: jaw purple
    ];

    public void SetFrame(string[] frame)
    {
        _frame = frame;
        InvalidateVisual();
        InvalidateMeasure();
    }

    public void SetPixelSize(int size)
    {
        _fontSize = size;
        InvalidateVisual();
        InvalidateMeasure();
    }

    private SolidColorBrush GetBrush(int row, int col)
    {
        if (row < ColorMap.Length && col < ColorMap[row].Length)
        {
            return ColorMap[row][col] switch
            {
                'P' => Purple,
                'C' => Cyan,
                'G' => Green,
                _ => Dark,
            };
        }
        return Dark;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_frame == null || _frame.Length == 0) return;

        var lineHeight = _fontSize * 1.2;

        // Measure single character width using monospace font
        var charMeasure = new FormattedText(
            "█", System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, _typeface, _fontSize, Dark);
        var charWidth = charMeasure.Width;

        for (int y = 0; y < _frame.Length; y++)
        {
            var row = _frame[y];
            int runStart = 0;

            while (runStart < row.Length)
            {
                // Find run of same color
                var brush = GetBrush(y, runStart);
                int runEnd = runStart + 1;
                while (runEnd < row.Length && GetBrush(y, runEnd) == brush)
                    runEnd++;

                var text = row[runStart..runEnd];
                var ft = new FormattedText(
                    text, System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, _typeface, _fontSize, brush);

                context.DrawText(ft, new Point(runStart * charWidth, y * lineHeight));
                runStart = runEnd;
            }
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_frame == null || _frame.Length == 0)
            return new Size(0, 0);

        var charMeasure = new FormattedText(
            "█", System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, _typeface, _fontSize, Dark);

        var maxCols = 0;
        foreach (var line in _frame)
            if (line.Length > maxCols) maxCols = line.Length;

        return new Size(maxCols * charMeasure.Width, _frame.Length * (_fontSize * 1.2));
    }
}
