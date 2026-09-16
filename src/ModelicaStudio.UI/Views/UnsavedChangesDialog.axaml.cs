using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ModelicaStudio.UI.Views;

public enum UnsavedChangesDecision
{
    Save,
    Discard,
    Cancel,
}

public sealed partial class UnsavedChangesDialog : Window
{
    public UnsavedChangesDialog()
        : this("the active Modelica document")
    {
    }

    public UnsavedChangesDialog(string documentName)
    {
        InitializeComponent();
        DocumentMessage.Text = $"{documentName} has unsaved changes.";
    }

    private void HandleSave(object? sender, RoutedEventArgs eventArgs) =>
        Close(UnsavedChangesDecision.Save);

    private void HandleDiscard(object? sender, RoutedEventArgs eventArgs) =>
        Close(UnsavedChangesDecision.Discard);

    private void HandleCancel(object? sender, RoutedEventArgs eventArgs) =>
        Close(UnsavedChangesDecision.Cancel);
}
