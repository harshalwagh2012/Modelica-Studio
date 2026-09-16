using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using ModelicaStudio.Domain.Diagnostics;

namespace ModelicaStudio.UI.Controls;

internal sealed class ModelicaDiagnosticRenderer : IBackgroundRenderer
{
    private readonly List<DiagnosticMarker> _markers = [];

    public KnownLayer Layer => KnownLayer.Background;

    public void SetDiagnostics(
        TextDocument document,
        IEnumerable<CompilerDiagnostic> diagnostics,
        string? documentPath,
        bool locationsAreStale)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(diagnostics);
        _markers.Clear();
        if (locationsAreStale)
        {
            return;
        }

        foreach (var diagnostic in diagnostics.Where(item =>
                     item.Line is not null && TargetsDocument(item.File, documentPath)))
        {
            var marker = CreateMarker(document, diagnostic);
            if (marker is not null)
            {
                _markers.Add(marker);
            }
        }
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(textView);
        ArgumentNullException.ThrowIfNull(drawingContext);
        if (_markers.Count == 0 || textView.VisualLines.Count == 0)
        {
            return;
        }

        var viewStart = textView.VisualLines[0].FirstDocumentLine.Offset;
        var viewEnd = textView.VisualLines[^1].LastDocumentLine.EndOffset;
        foreach (var marker in _markers.Where(item =>
                     item.EndOffset >= viewStart && item.StartOffset <= viewEnd))
        {
            marker.Draw(textView, drawingContext);
        }
    }

    public static bool TargetsDocument(string? diagnosticFile, string? documentPath)
    {
        if (string.IsNullOrWhiteSpace(diagnosticFile) || diagnosticFile == "<interactive>")
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(documentPath))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(diagnosticFile),
                Path.GetFullPath(documentPath),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static DiagnosticMarker? CreateMarker(TextDocument document, CompilerDiagnostic diagnostic)
    {
        var lineNumber = Math.Clamp(diagnostic.Line ?? 1, 1, document.LineCount);
        var line = document.GetLineByNumber(lineNumber);
        var column = Math.Clamp(diagnostic.Column ?? 1, 1, line.Length + 1);
        var startOffset = Math.Min(document.TextLength, line.Offset + column - 1);

        var endOffset = line.EndOffset;
        if (diagnostic.EndLine is { } requestedEndLine)
        {
            var endLine = document.GetLineByNumber(Math.Clamp(requestedEndLine, lineNumber, document.LineCount));
            var endColumn = Math.Clamp(diagnostic.EndColumn ?? endLine.Length, 1, endLine.Length + 1);
            endOffset = Math.Min(document.TextLength, endLine.Offset + endColumn);
        }

        if (endOffset <= startOffset)
        {
            endOffset = Math.Min(document.TextLength, startOffset + 1);
        }

        if (endOffset <= startOffset)
        {
            return null;
        }

        return new DiagnosticMarker(
            startOffset,
            endOffset - startOffset,
            diagnostic.Severity switch
            {
                CompilerDiagnosticSeverity.Error => Color.FromRgb(190, 39, 45),
                CompilerDiagnosticSeverity.Warning => Color.FromRgb(196, 118, 16),
                CompilerDiagnosticSeverity.Notification => Color.FromRgb(35, 105, 165),
                _ => Color.FromRgb(116, 82, 151),
            });
    }

    private sealed class DiagnosticMarker : TextSegment
    {
        private readonly Pen _pen;

        public DiagnosticMarker(int startOffset, int length, Color color)
        {
            StartOffset = startOffset;
            Length = length;
            _pen = new Pen(new SolidColorBrush(color), 1);
        }

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            foreach (var rectangle in BackgroundGeometryBuilder.GetRectsForSegment(textView, this))
            {
                if (rectangle.Width <= 1 || rectangle.Height <= 1)
                {
                    continue;
                }

                var start = rectangle.BottomLeft;
                var width = Math.Max(2, rectangle.Width);
                var zigCount = Math.Max(2, (int)Math.Round(width / 3));
                var geometry = new StreamGeometry();
                using (var geometryContext = geometry.Open())
                {
                    geometryContext.BeginFigure(start, false);
                    for (var index = 1; index <= zigCount; index++)
                    {
                        geometryContext.LineTo(new Point(
                            start.X + (width * index / zigCount),
                            start.Y - ((index % 2) * 2) + 1));
                    }
                }

                drawingContext.DrawGeometry(null, _pen, geometry);
            }
        }
    }
}
