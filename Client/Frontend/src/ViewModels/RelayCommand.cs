namespace Markwardt.ClarityJournal.Client;

/// <summary>
/// A command that runs a synchronous action.
/// </summary>
/// <param name="execute">The action to run; it receives the command parameter.</param>
/// <param name="canExecute">Whether the command is currently allowed, if it is ever not.</param>
internal sealed class RelayCommand(Action<object?> execute, Func<bool>? canExecute = null) : ICommand
{
    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    /// <inheritdoc />
    public void Execute(object? parameter) => execute(parameter);

    /// <summary>
    /// Re-evaluates whether the command may run.
    /// </summary>
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
