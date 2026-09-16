using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelicaStudio.Application.Engine;
using ModelicaStudio.Application.Files;
using ModelicaStudio.Application.Modeling;
using ModelicaStudio.Application.Projects;
using ModelicaStudio.Application.Settings;
using ModelicaStudio.Infrastructure.Files;
using ModelicaStudio.Infrastructure.Modeling;
using ModelicaStudio.Infrastructure.Projects;
using ModelicaStudio.Infrastructure.Settings;
using ModelicaStudio.OpenModelica.Engine;
using ModelicaStudio.OpenModelica.Protocol;
using ModelicaStudio.UI.ViewModels;
using ModelicaStudio.UI.Views;

namespace ModelicaStudio.Desktop;

public sealed partial class App : Avalonia.Application
{
    private ServiceProvider? _services;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _services = ConfigureServices();
            desktop.MainWindow = _services.GetRequiredService<MainWindow>();
            desktop.Exit += HandleExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        var fileWriter = new AtomicFileWriter();
        var settingsService = new JsonApplicationSettingsService(fileWriter, ApplicationSettingsPath.GetDefault());
        var settings = settingsService.LoadAsync().GetAwaiter().GetResult();
        services.AddSingleton(new OpenModelicaOptions { OmcPath = settings.OpenModelicaExecutablePath });
        services.AddSingleton<IOpenModelicaInstallationLocator, OpenModelicaInstallationLocator>();
        services.AddSingleton<IOmcTransportFactory, OpenModelicaZmqTransportFactory>();
        services.AddSingleton<IOpenModelicaService, OpenModelicaZmqService>();
        services.AddSingleton<IAtomicFileWriter>(fileWriter);
        services.AddSingleton<IApplicationSettingsService>(settingsService);
        services.AddSingleton<IProjectService, ProjectService>();
        services.AddSingleton<IModelicaDocumentService, ModelicaDocumentService>();
        services.AddSingleton<IModelicaSourceAnalyzer, ModelicaSourceAnalyzer>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
        return services.BuildServiceProvider(validateScopes: true);
    }

    private async void HandleExit(object? sender, ControlledApplicationLifetimeExitEventArgs eventArgs)
    {
        if (_services is not null)
        {
            await _services.DisposeAsync().ConfigureAwait(false);
        }

        _services = null;
    }
}
