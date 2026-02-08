using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CopilotVoice.Views;

/// <summary>
/// Custom Avalonia control that renders pixel art from a 2D char grid.
/// Each character maps to a color via PixelAvatarData.GetColor().
/// </summary>
public class PixelAvatarControl : Control
{
    private string[]? _frame;
    private int _pixelSize = 12;

    public void SetFrame(string[] frame)
    {
        _frame = frame;
        InvalidateVisual();
    }

    public void SetPixelSize(int size)
    {
        _pixelSize = size;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_frame == null || _frame.Length == 0) return;

        var rows = _frame.Length;
        var cols = _frame[0].Length;
        var ps = _pixelSize;

        for (int y = 0; y < rows; y++)
        {
            var row = _frame[y];
            for (int x = 0; x < row.Length; x++)
            {
                var colorHex = UI.Avatar.PixelAvatarData.GetColor(row[x]);
                if (colorHex == UI.Avatar.PixelAvatarData.ColorTransparent)
                    continue;

                var color = Color.Parse(colorHex);
                var brush = new SolidColorBrush(color);
                var rect = new Rect(x * ps, y * ps, ps, ps);
                context.FillRectangle(brush, rect);
            }
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_frame == null || _frame.Length == 0)
            return new Size(0, 0);

        var cols = _frame[0].Length;
        var rows = _frame.Length;
        return new Size(cols * _pixelSize, rows * _pixelSize);
    }
}
