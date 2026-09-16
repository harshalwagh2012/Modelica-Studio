using ModelicaStudio.Application.Commands;

namespace ModelicaStudio.Application.Tests;

public sealed class ModelCommandHistoryTests
{
    [Fact]
    public async Task ExecuteUndoRedo_MovesOnlySuccessfulCommandBetweenStacks()
    {
        var value = 0;
        var history = new ModelCommandHistory();
        var command = new ReversibleModelCommand(
            "Move plant",
            _ =>
            {
                value = 12;
                return Task.CompletedTask;
            },
            _ =>
            {
                value = 0;
                return Task.CompletedTask;
            });

        await history.ExecuteAsync(command);

        Assert.Equal(12, value);
        Assert.True(history.CanUndo);
        Assert.Equal("Move plant", history.UndoDescription);
        Assert.False(history.CanRedo);

        Assert.True(await history.UndoAsync());
        Assert.Equal(0, value);
        Assert.False(history.CanUndo);
        Assert.True(history.CanRedo);

        Assert.True(await history.RedoAsync());
        Assert.Equal(12, value);
        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public async Task FailedExecute_IsNotRecordedAndPreservesRedoStack()
    {
        var history = new ModelCommandHistory();
        var command = new ReversibleModelCommand("First", _ => Task.CompletedTask, _ => Task.CompletedTask);
        await history.ExecuteAsync(command);
        await history.UndoAsync();
        var failure = new ReversibleModelCommand(
            "Failure",
            _ => throw new InvalidOperationException("rejected"),
            _ => Task.CompletedTask);

        await Assert.ThrowsAsync<InvalidOperationException>(() => history.ExecuteAsync(failure));

        Assert.False(history.CanUndo);
        Assert.True(history.CanRedo);
        Assert.Equal("First", history.RedoDescription);
    }

    [Fact]
    public async Task FailedUndo_RemainsUndoable()
    {
        var history = new ModelCommandHistory();
        await history.ExecuteAsync(new ReversibleModelCommand(
            "Move",
            _ => Task.CompletedTask,
            _ => throw new InvalidOperationException("rollback rejected")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => history.UndoAsync());

        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);
    }
}
