namespace CreatorRizz.Application.Abstractions;

/// <summary>
/// Commits a state change and the work that change implies as one unit. Without this, a crash between
/// the two leaves a production in a state that waits for work nobody will ever deliver.
/// </summary>
public interface IWorkflowTransaction
{
    Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken);
}
