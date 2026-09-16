using Avalonia.Controls;
using Avalonia.Interactivity;
using ModelicaStudio.UI.ViewModels;

namespace ModelicaStudio.UI.Views;

public sealed partial class SimulationSetupDialog : Window
{
    private readonly SimulationSetupViewModel _viewModel;

    public SimulationSetupDialog()
        : this(new ModelicaStudio.Domain.Simulation.SimulationConfiguration())
    {
    }

    public SimulationSetupDialog(ModelicaStudio.Domain.Simulation.SimulationConfiguration configuration)
    {
        InitializeComponent();
        _viewModel = new SimulationSetupViewModel(configuration);
        DataContext = _viewModel;
    }

    private void HandleCancel(object? sender, RoutedEventArgs eventArgs) => Close(null);

    private void HandleSave(object? sender, RoutedEventArgs eventArgs) => Complete(runImmediately: false);

    private void HandleRun(object? sender, RoutedEventArgs eventArgs) => Complete(runImmediately: true);

    private void Complete(bool runImmediately)
    {
        if (_viewModel.TryCreateConfiguration(out var configuration) && configuration is not null)
        {
            Close(new SimulationSetupDecision(configuration, runImmediately));
        }
    }
}
