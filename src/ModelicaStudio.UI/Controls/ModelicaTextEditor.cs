using System.Reflection;
using System.Xml;
using Avalonia;
using Avalonia.Data;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using AvaloniaEdit.Search;
using ModelicaStudio.Domain.Diagnostics;

namespace ModelicaStudio.UI.Controls;

public sealed class ModelicaTextEditor : TextEditor
{
    private const string HighlightingResourceName = "ModelicaStudio.UI.Assets.Modelica.xshd";
    private readonly SearchPanel _searchPanel;
    private readonly ModelicaDiagnosticRenderer _diagnosticRenderer;
    private bool _isSynchronizingText;
    private string _sourceText = string.Empty;

    public static readonly DirectProperty<ModelicaTextEditor, string> SourceTextProperty =
        AvaloniaProperty.RegisterDirect<ModelicaTextEditor, string>(
            nameof(SourceText),
            editor => editor.SourceText,
            (editor, value) => editor.SourceText = value,
            defaultBindingMode: BindingMode.TwoWay);

    public ModelicaTextEditor()
    {
        ShowLineNumbers = true;
        SyntaxHighlighting = LoadModelicaHighlighting();
        Options.HighlightCurrentLine = true;
        Options.ConvertTabsToSpaces = true;
        Options.IndentationSize = 2;
        Options.EnableTextDragDrop = true;
        Options.AllowToggleOverstrikeMode = true;
        TextArea.IndentationStrategy = new ModelicaIndentationStrategy(Options.IndentationSize);
        TextArea.RightClickMovesCaret = true;
        TextArea.TextView.CurrentLineBorder = new Pen(new SolidColorBrush(Color.FromRgb(224, 226, 228)));
        _searchPanel = SearchPanel.Install(this);
        _diagnosticRenderer = new ModelicaDiagnosticRenderer();
        TextArea.TextView.BackgroundRenderers.Add(_diagnosticRenderer);
        TextChanged += HandleTextChanged;
    }

    public string SourceText
    {
        get => _sourceText;
        set
        {
            value ??= string.Empty;
            if (!SetAndRaise(SourceTextProperty, ref _sourceText, value)
                || _isSynchronizingText
                || string.Equals(Text, value, StringComparison.Ordinal))
            {
                return;
            }

            _isSynchronizingText = true;
            try
            {
                Text = value;
                Document.UndoStack.ClearAll();
            }
            finally
            {
                _isSynchronizingText = false;
            }
        }
    }

    public void OpenSearch() => _searchPanel.Open();

    public void SetDiagnostics(
        IEnumerable<CompilerDiagnostic> diagnostics,
        string? documentPath,
        bool locationsAreStale)
    {
        _diagnosticRenderer.SetDiagnostics(Document, diagnostics, documentPath, locationsAreStale);
        TextArea.TextView.InvalidateLayer(_diagnosticRenderer.Layer);
    }

    public bool NavigateTo(CompilerDiagnostic diagnostic, string? documentPath)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        if (diagnostic.Line is null
            || !ModelicaDiagnosticRenderer.TargetsDocument(diagnostic.File, documentPath))
        {
            return false;
        }

        var lineNumber = Math.Clamp(diagnostic.Line.Value, 1, Document.LineCount);
        var line = Document.GetLineByNumber(lineNumber);
        var column = Math.Clamp(diagnostic.Column ?? 1, 1, line.Length + 1);
        var startOffset = line.Offset + column - 1;
        var selectionLength = 0;
        if (diagnostic.EndLine == lineNumber && diagnostic.EndColumn is { } endColumn)
        {
            selectionLength = Math.Max(0, Math.Min(line.EndOffset, line.Offset + endColumn) - startOffset);
        }

        Select(startOffset, selectionLength);
        CaretOffset = startOffset;
        ScrollTo(lineNumber, column);
        Focus();
        return true;
    }

    protected override Type StyleKeyOverride => typeof(TextEditor);

    private void HandleTextChanged(object? sender, EventArgs eventArgs)
    {
        if (_isSynchronizingText)
        {
            return;
        }

        _isSynchronizingText = true;
        try
        {
            SourceText = Text ?? string.Empty;
        }
        finally
        {
            _isSynchronizingText = false;
        }
    }

    private static IHighlightingDefinition LoadModelicaHighlighting()
    {
        var assembly = typeof(ModelicaTextEditor).Assembly;
        using var stream = assembly.GetManifestResourceStream(HighlightingResourceName)
            ?? throw new InvalidOperationException($"Embedded highlighting resource '{HighlightingResourceName}' was not found.");
        using var reader = XmlReader.Create(stream);
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }
}
