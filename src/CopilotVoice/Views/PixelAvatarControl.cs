using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CopilotVoice.Views;

/// <summary>
/// Custom Avalonia control that renders the Copilot CLI robot as ASCII art.
/// </summary>
public class PixelAvatarControl : Control
{
    private string[]? _frame;
    private double _fontSize = 14;
    private readonly Typeface _typeface = new("Cascadia Mono, Menlo, Consolas, monospace");

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

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_frame == null || _frame.Length == 0) return;

        var foreground = new SolidColorBrush(Color.Parse("#CDD6F4"));

        for (int y = 0; y < _frame.Length; y++)
        {
            var formattedText = new FormattedText(
                _frame[y],
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                _typeface,
                _fontSize,
                foreground);

            context.DrawText(formattedText, new Point(0, y * (_fontSize * 1.2)));
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_frame == null || _frame.Length == 0)
            return new Size(0, 0);

        // Measure using the widest line
        var foreground = new SolidColorBrush(Color.Parse("#CDD6F4"));
        double maxWidth = 0;
        foreach (var line in _frame)
        {
            var ft = new FormattedText(
                line,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                _typeface,
                _fontSize,
                foreground);
            if (ft.Width > maxWidth) maxWidth = ft.Width;
        }

        return new Size(maxWidth, _frame.Length * (_fontSize * 1.2));
    }
}
