using System.Collections.Concurrent;
using System.Management.Automation;

namespace AsyncPS;

public class AsyncPSCmdlet : PSCmdlet
{
    #region Dispatch into main thread

    private readonly BlockingCollection<Action> dispatcherQueue = new();
    private readonly int ownerThreadId = Thread.CurrentThread.ManagedThreadId;

    /// <summary>
    /// Invokes the <paramref name="action"/> in the dispatcher thread
    /// </summary>
    public void Dispatch(Action action)
    {
        if (this.ownerThreadId == Thread.CurrentThread.ManagedThreadId)
            action?.Invoke();
        else if (action is { } notNull)
            this.dispatcherQueue.Add(notNull);
    }

    #endregion Dispatch into main thread

    #region Cancel processing records

    /// <summary>
	/// Cancellation toke source associated to the Ctrl-C event
	/// </summary>
    protected CancellationTokenSource ControlCTokenSource { get; } = new();

    /// <summary>
    /// Propagates Ctrl-C to async operations
    /// </summary>
    protected override void StopProcessing() => this.ControlCTokenSource.Cancel();

    protected override void EndProcessing() => this.ControlCTokenSource.Dispose();

    #endregion Cancel processing records

    #region Adapts calls to sync ProcessRecord to async ProcessRecordAsync

    protected override void ProcessRecord()
    {
        var cancellationToken = this.ControlCTokenSource.Token;
        try
        {
            // run the async operation from this STA/foreground thread at the thread pool.
            var processRecordInBackground = Task
                .Run(async () => await this.ProcessRecordAsync(cancellationToken), cancellationToken)
                .ContinueWith(t =>
                {
                    if(t.IsFaulted)
                        this.Dispatch(()=>this.WriteError(new ErrorRecord(
                            exception:t.Exception.InnerException,
                            errorId:"psasynccmdlet-task-failed",
                            targetObject:null,
                            errorCategory:ErrorCategory.OperationStopped)));
                    this.dispatcherQueue.CompleteAdding();
                });

            // execute dispatched actions in STA/foreground thread
            foreach (var action in this.dispatcherQueue.GetConsumingEnumerable(cancellationToken))
                action?.Invoke();

            // await the finalization of the BG task
            processRecordInBackground.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// Override instead of <see cref="ProcessRecord"/> to implement async processing of records
    /// </summary>
    protected virtual Task ProcessRecordAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    #endregion Adapts calls to sync ProcessRecord to async ProcessRecordAsync
}
