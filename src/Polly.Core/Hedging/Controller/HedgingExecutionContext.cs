using Polly.Hedging.Controller;

namespace Polly.Hedging.Utils;

/// <summary>
/// The context associated with an execution of hedging resilience strategy.
/// It holds the resources for all executed hedged tasks (primary + secondary) and is responsible for resource disposal.
/// </summary>
internal sealed class HedgingExecutionContext<T> : IAsyncDisposable
{
    public readonly record struct ExecutionInfo<TResult>(TaskExecution<T>? Execution, bool Loaded, Outcome<TResult>? Outcome);

    private readonly List<TaskExecution<T>> _tasks = new();
    private readonly List<TaskExecution<T>> _executingTasks = new();
    private readonly ObjectPool<TaskExecution<T>> _executionPool;
    private readonly TimeProvider _timeProvider;
    private readonly int _maxAttempts;
    private readonly Action<HedgingExecutionContext<T>> _onReset;

    public HedgingExecutionContext(
        ObjectPool<TaskExecution<T>> executionPool,
        TimeProvider timeProvider,
        int maxAttempts,
        Action<HedgingExecutionContext<T>> onReset)
    {
        _executionPool = executionPool;
        _timeProvider = timeProvider;
        _maxAttempts = maxAttempts;
        _onReset = onReset;
    }

    internal void Initialize(ResilienceContext context) => PrimaryContext = context;

    public int LoadedTasks => _tasks.Count;

    public ResilienceContext? PrimaryContext { get; private set; }

    public bool IsInitialized => PrimaryContext != null;

    public IReadOnlyList<TaskExecution<T>> Tasks => _tasks;

    private bool ContinueOnCapturedContext => PrimaryContext!.ContinueOnCapturedContext;

    public async ValueTask<ExecutionInfo<T>> LoadExecutionAsync<TState>(
        Func<ResilienceContext, TState, ValueTask<Outcome<T>>> primaryCallback,
        TState state)
    {
        if (LoadedTasks >= _maxAttempts)
        {
            return CreateExecutionInfoWhenNoExecution();
        }

        // determine what type of task we are creating
        var type = LoadedTasks switch
        {
            0 => HedgedTaskType.Primary,
            _ => HedgedTaskType.Secondary
        };

        var execution = _executionPool.Get();

        if (await execution.InitializeAsync(type, PrimaryContext!, primaryCallback, state, LoadedTasks).ConfigureAwait(ContinueOnCapturedContext))
        {
            // we were able to start a new execution, register it
            _tasks.Add(execution);
            _executingTasks.Add(execution);
            return new ExecutionInfo<T>(execution, true, null);
        }
        else
        {
            _executionPool.Return(execution);
            return CreateExecutionInfoWhenNoExecution();
        }
    }

    public async ValueTask DisposeAsync()
    {
        UpdateOriginalContext();

        // first, cancel any pending tasks
        foreach (var pair in _executingTasks)
        {
            pair.Cancel();
        }

        foreach (var task in _tasks)
        {
            await task.ExecutionTaskSafe!.ConfigureAwait(false);
            await task.ResetAsync().ConfigureAwait(false);
            _executionPool.Return(task);
        }

        Reset();
    }

    public async ValueTask<TaskExecution<T>?> TryWaitForCompletedExecutionAsync(TimeSpan hedgingDelay)
    {
        // before doing anything expensive, let's check whether any existing task is already completed
        if (TryRemoveExecutedTask() is TaskExecution<T> execution)
        {
            return execution;
        }

        if (LoadedTasks == _maxAttempts)
        {
            await WaitForTaskCompetitionAsync().ConfigureAwait(ContinueOnCapturedContext);
            return TryRemoveExecutedTask();
        }

        if (hedgingDelay == TimeSpan.Zero || LoadedTasks == 0)
        {
            // just load the next task
            return null;
        }

        // Stryker disable once equality : no means to test this, stryker changes '<' to '<=' where 0 is already covered in the branch above
        if (hedgingDelay < TimeSpan.Zero)
        {
            await WaitForTaskCompetitionAsync().ConfigureAwait(ContinueOnCapturedContext);
            return TryRemoveExecutedTask();
        }

        using var delayTaskCancellation = CancellationTokenSource.CreateLinkedTokenSource(PrimaryContext!.CancellationToken);

        var delayTask = _timeProvider.Delay(hedgingDelay, delayTaskCancellation.Token);
        Task<Task> whenAnyHedgedTask = WaitForTaskCompetitionAsync();
        var completedTask = await Task.WhenAny(whenAnyHedgedTask, delayTask).ConfigureAwait(ContinueOnCapturedContext);

        if (completedTask == delayTask)
        {
            return null;
        }

        // cancel the ongoing delay task
        // Stryker disable once boolean : no means to test this
        delayTaskCancellation.Cancel(throwOnFirstException: false);

        await whenAnyHedgedTask.ConfigureAwait(ContinueOnCapturedContext);

        return TryRemoveExecutedTask();
    }

    private ExecutionInfo<T> CreateExecutionInfoWhenNoExecution()
    {
        // if there are no more executing tasks we need to check finished ones
        if (_executingTasks.Count == 0)
        {
            var finishedExecution = _tasks.First(static t => t.ExecutionTaskSafe!.IsCompleted);
            finishedExecution.AcceptOutcome();

            return new ExecutionInfo<T>(null, false, finishedExecution.Outcome);
        }

        return new ExecutionInfo<T>(null, false, null);
    }

    private Task<Task> WaitForTaskCompetitionAsync()
    {
#pragma warning disable S109 // Magic numbers should not be used
        return _executingTasks.Count switch
        {
            1 => AwaitTask(_executingTasks[0], ContinueOnCapturedContext),
            2 => Task.WhenAny(_executingTasks[0].ExecutionTaskSafe!, _executingTasks[1].ExecutionTaskSafe!),
            _ => Task.WhenAny(_executingTasks.Select(v => v.ExecutionTaskSafe!))
        };
#pragma warning restore S109 // Magic numbers should not be used

        static async Task<Task> AwaitTask(TaskExecution<T> task, bool continueOnCapturedContext)
        {
            // ExecutionTask never fails
            await task.ExecutionTaskSafe!.ConfigureAwait(continueOnCapturedContext);
            return Task.FromResult(task);
        }
    }

    private TaskExecution<T>? TryRemoveExecutedTask()
    {
        if (_executingTasks.Find(static v => v.ExecutionTaskSafe!.IsCompleted) is TaskExecution<T> execution)
        {
            _executingTasks.Remove(execution);
            return execution;
        }

        return null;
    }

    private void UpdateOriginalContext()
    {
        if (LoadedTasks == 0)
        {
            return;
        }

        Debug.Assert(Tasks.Count(t => t.IsAccepted) == 1, $"There must be exactly one accepted outcome for hedging. Found {Tasks.Count(t => t.IsAccepted)}.");

        if (Tasks.FirstOrDefault(static t => t.IsAccepted) is TaskExecution<T> acceptedExecution)
        {
            PrimaryContext!.Properties.AddOrReplaceProperties(acceptedExecution.Context.Properties);
        }
    }

    private void Reset()
    {
        _tasks.Clear();

        _executingTasks.Clear();
        PrimaryContext = null;

        _onReset(this);
    }
}
