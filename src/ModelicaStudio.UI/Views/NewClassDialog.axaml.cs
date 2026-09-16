using Avalonia.Controls;
using Avalonia.Interactivity;
using ModelicaStudio.Application.Modeling;
using ModelicaStudio.Domain.Modeling;

namespace ModelicaStudio.UI.Views;

public sealed partial class NewClassDialog : Window
{
    public NewClassDialog()
        : this(ModelicaClassType.Model, null)
    {
    }

    public NewClassDialog(ModelicaClassType initialType, string? withinPackage)
    {
        InitializeComponent();
        ClassTypeField.ItemsSource = Enum.GetValues<ModelicaClassType>();
        ClassTypeField.SelectedItem = initialType;
        WithinField.Text = withinPackage;
    }

    private void HandleCancel(object? sender, RoutedEventArgs eventArgs) => Close(null);

    private void HandleCreate(object? sender, RoutedEventArgs eventArgs)
    {
        var definition = new NewModelicaClass(
            NameField.Text?.Trim() ?? string.Empty,
            ClassTypeField.SelectedItem is ModelicaClassType type ? type : ModelicaClassType.Model,
            NullIfWhiteSpace(DescriptionField.Text),
            NullIfWhiteSpace(WithinField.Text));
        try
        {
            _ = ModelicaSourceGenerator.Generate(definition);
            Close(definition);
        }
        catch (ArgumentException exception)
        {
            ValidationMessage.Text = exception.Message;
            ValidationMessage.IsVisible = true;
        }
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
