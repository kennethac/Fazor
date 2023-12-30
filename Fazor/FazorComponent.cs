using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Fazor;

public abstract class FazorComponent : IComponent
{
    private readonly RenderFragment _renderFragment;
    private bool _hasPendingRender = false;

    public FazorComponent()
    {
        _renderFragment = builder =>
        {
            _hasPendingRender = false;
            var fragment = GetRenderFragment();
            fragment(builder);
        };
    }

    /// <summary>
    /// This method exists only so that the Razor source generator has a suitable method to override.
    /// </summary>
    /// <param name="builder"></param>
    protected virtual void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.AddContent(1, "There has been an error!");
    }

    private RenderHandle _renderHandle;

    public void Attach(RenderHandle renderHandle)
    {
        _renderHandle = !_renderHandle.IsInitialized
            ? renderHandle
            : throw new InvalidOperationException(
                "The render handle is already set. Cannot initialize a FazorComponent more than once.");
    }

    public Task SetParametersAsync(ParameterView parameters)
    {
        parameters.SetParameterProperties(this);
        TriggerRender();
        return Task.CompletedTask;
    }

    protected void TriggerRender()
    {
        if (_hasPendingRender)
        {
            return;
        }

        _hasPendingRender = true;
        try
        {
            if (!_renderHandle.Dispatcher.CheckAccess())
            {
                _renderHandle.Dispatcher.InvokeAsync(() => _renderHandle.Render(_renderFragment));
            }
            else
            {
                _renderHandle.Render(_renderFragment);
            }
        }
        catch (Exception e)
        {
            _hasPendingRender = false;
            throw;
        }
    }

    protected abstract RenderFragment GetRenderFragment();

    #region State Management Methods

    private readonly ConcurrentDictionary<int, object> _states = new();
    private readonly ConcurrentDictionary<int, (object, object)> _asyncStates = new();
    private readonly HashSet<Task> _tasksBeingWatched = new();

    private Task<T> WatchTask<T>(Task<T> task)
    {
        if (_tasksBeingWatched.Contains(task) || task.IsCompleted || task.IsFaulted) return task;
        _ = Task.Run(async () =>
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            finally
            {
                _tasksBeingWatched.Remove(task);
                TriggerRender();
            }
        });
        _tasksBeingWatched.Add(task);
        return task;
    }

    private FazorState<TResult, Exception> TaskToState<TResult>(Task<TResult> task)
    {
        return WatchTask(task) switch
        {
            { IsCompletedSuccessfully: true, Result: var result } => new FazorState<TResult, Exception>.Success(result),
            { IsCompleted: true, Exception: var exception } => new FazorState<TResult, Exception>.Failure(
                exception ?? new Exception("Testing....")),
            _ => FazorState<TResult, Exception>.Loading.Instance
        };
    }

    protected MutableState<TOk> UseState<TOk>(TOk initialValue, [CallerLineNumber] int callerLineNumber = 0)
    {
        var value = _states.GetOrAdd(callerLineNumber, _ => initialValue!);
        return new MutableState<TOk>()
        {
            Value = (TOk)value,
            Update = newValue =>
            {
                _states.TryUpdate(callerLineNumber, newValue!, value);
                TriggerRender();
            }
        };
    }

    protected FazorState<TOk, Exception> Derive<TInput, TOk>(Func<TInput, Task<TOk>> derivation, TInput input,
        [CallerLineNumber] int callerLineNumber = 0)
    {
        var thisInput = new FazorState<TInput, Exception>.Success(input);
        var (lastInputObject, derivationTask)
            = ((FazorState<TInput, Exception>, Task<TOk>))_asyncStates.GetOrAdd(
                callerLineNumber,
                _ => (thisInput, derivation(input)));
        if (lastInputObject!.Equals(thisInput)) return TaskToState(derivationTask);

        var newDerivation = derivation(input);
        _asyncStates.AddOrUpdate(callerLineNumber,
            _ => (thisInput, newDerivation),
            (_, _) => (thisInput, newDerivation));
        return TaskToState(newDerivation);
    }

    #endregion
}

public record MutableState<TOk>
{
    public required TOk Value { get; init; }
    public required Action<TOk> Update { get; init; }

    public void Deconstruct(out TOk value, out Action<TOk> update)
    {
        value = Value;
        update = Update;
    }
}

public abstract record FazorState<TOk, TErr>
{
    public record Loading : FazorState<TOk, TErr>
    {
        private Loading()
        {
        }

        public static readonly Loading Instance = new();
    }

    public record Success(TOk Value) : FazorState<TOk, TErr>;

    public record Failure(TErr Value) : FazorState<TOk, TErr>;

    public Success? AsSuccess() => this as Success;
    public Failure? AsFailure() => this as Failure;
    public Loading? AsLoading() => this as Loading;
}