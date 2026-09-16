using ModelicaStudio.Domain.Simulation;

namespace ModelicaStudio.UI.ViewModels;

public sealed class SimulationSetupViewModel : ObservableObject
{
    private decimal _startTime;
    private decimal _stopTime;
    private int _numberOfIntervals;
    private decimal _tolerance;
    private string _method;
    private string _outputFormat;
    private string _variableFilter;
    private string _validationMessage = string.Empty;

    public SimulationSetupViewModel(SimulationConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _startTime = (decimal)configuration.StartTime;
        _stopTime = (decimal)configuration.StopTime;
        _numberOfIntervals = configuration.NumberOfIntervals;
        _tolerance = (decimal)configuration.Tolerance;
        _method = configuration.Method ?? string.Empty;
        _outputFormat = configuration.OutputFormat;
        _variableFilter = configuration.VariableFilter ?? string.Empty;
    }

    public decimal StartTime
    {
        get => _startTime;
        set
        {
            if (SetProperty(ref _startTime, value))
            {
                OnPropertyChanged(nameof(Interval));
            }
        }
    }

    public decimal StopTime
    {
        get => _stopTime;
        set
        {
            if (SetProperty(ref _stopTime, value))
            {
                OnPropertyChanged(nameof(Interval));
            }
        }
    }

    public int NumberOfIntervals
    {
        get => _numberOfIntervals;
        set
        {
            if (SetProperty(ref _numberOfIntervals, value))
            {
                OnPropertyChanged(nameof(Interval));
            }
        }
    }

    public decimal Tolerance
    {
        get => _tolerance;
        set => SetProperty(ref _tolerance, value);
    }

    public string Method
    {
        get => _method;
        set => SetProperty(ref _method, value ?? string.Empty);
    }

    public string OutputFormat
    {
        get => _outputFormat;
        set => SetProperty(ref _outputFormat, value ?? string.Empty);
    }

    public string VariableFilter
    {
        get => _variableFilter;
        set => SetProperty(ref _variableFilter, value ?? string.Empty);
    }

    public decimal? Interval => NumberOfIntervals > 0
        ? (StopTime - StartTime) / NumberOfIntervals
        : null;

    public string ValidationMessage
    {
        get => _validationMessage;
        private set
        {
            if (SetProperty(ref _validationMessage, value))
            {
                OnPropertyChanged(nameof(HasValidationError));
            }
        }
    }

    public bool HasValidationError => ValidationMessage.Length > 0;

    public bool TryCreateConfiguration(out SimulationConfiguration? configuration)
    {
        configuration = new SimulationConfiguration
        {
            StartTime = (double)StartTime,
            StopTime = (double)StopTime,
            NumberOfIntervals = NumberOfIntervals,
            Tolerance = (double)Tolerance,
            Method = string.IsNullOrWhiteSpace(Method) ? null : Method.Trim(),
            OutputFormat = OutputFormat.Trim(),
            VariableFilter = string.IsNullOrWhiteSpace(VariableFilter) ? null : VariableFilter.Trim(),
        };

        try
        {
            configuration.Validate();
            ValidationMessage = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException)
        {
            ValidationMessage = exception.Message;
            configuration = null;
            return false;
        }
    }
}

public sealed record SimulationSetupDecision(SimulationConfiguration Configuration, bool RunImmediately);
