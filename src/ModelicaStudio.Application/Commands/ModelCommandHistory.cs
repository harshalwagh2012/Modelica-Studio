namespace ModelicaStudio.Application.Commands;

public interface IReversibleModelCommand
{
    string Description { get; }

    Task ExecuteAsync(CancellationToken cancellationToken = default);

    Task UndoAsync(CancellationToken cancellationToken = default);
}

public sealed class ReversibleModelCommand(
    string description,
    Func<CancellationToken, Task> execute,
    Func<CancellationToken, Task> undo) : IReversibleModelCommand
{
    public string Description { get; } = !string.IsNullOrWhiteSpace(description)
        ? description
        : throw new ArgumentException("A command description is required.", nameof(description));

    public Task ExecuteAsync(CancellationToken cancellationToken = default) =>
        execute(cancellationToken);

    public Task UndoAsync(CancellationToken cancellationToken = default) =>
        undo(cancellationToken);
}

public sealed class ModelCommandHistory
{
    private readonly Stack<IReversibleModelCommand> _undo = [];
    private readonly Stack<IReversibleModelCommand> _redo = [];
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoDescription => _undo.TryPeek(out var command) ? command.Description : null;
    public string? RedoDescription => _redo.TryPeek(out var command) ? command.Description : null;

    public async Task ExecuteAsync(
        IReversibleModelCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await command.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            _undo.Push(command);
            _redo.Clear();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> UndoAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_undo.TryPeek(out var command))
            {
                return false;
            }

            await command.UndoAsync(cancellationToken).ConfigureAwait(false);
            _undo.Pop();
            _redo.Push(command);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> RedoAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_redo.TryPeek(out var command))
            {
                return false;
            }

            await command.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            _redo.Pop();
            _undo.Push(command);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
