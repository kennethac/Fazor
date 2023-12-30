using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Fazor;

public abstract class FazorComponent : IComponent
{
    protected virtual void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.AddContent(1, "There has been an error!");
    }
    
    private RenderHandle _renderHandle;
    // private bool _initialized = false;

    public void Attach(RenderHandle renderHandle)
    {
        _renderHandle = !_renderHandle.IsInitialized
            ? renderHandle
            : throw new InvalidOperationException(
                "The render handle is already set. Cannot initialize a FazorComponent more than once.");
    }

    public Task SetParametersAsync(ParameterView parameters)
    {
        // parameters.GetValueOrDefault()
        parameters.SetParameterProperties(this);
        TriggerRender();
        return Task.CompletedTask;
    }

    private void TriggerRender()
    {
        _renderHandle.Render(GetRenderFragment());
    }

    protected abstract RenderFragment GetRenderFragment();

    #region State Management Methods

    private ConcurrentDictionary<int, object> _states = new();

    public MutableState<TOk> UseState<TOk>(TOk initialValue, [CallerLineNumber] int callerLineNumber = 0)
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

        public static Loading Instance = new();
    }

    public record Success(TOk Value) : FazorState<TOk, TErr>;

    public record Failure(TErr Value) : FazorState<TOk, TErr>;
}