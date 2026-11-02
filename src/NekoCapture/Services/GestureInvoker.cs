using NekoCapture.Core;
using GC = GestureCore;

namespace NekoCapture.Services;

/// <summary>将行为入口或动作链投递到 UI 线程，输入钩子内只做受理判定。</summary>
sealed class GestureInvoker : GC.IChainInvoker
{
    readonly AppHost _host;

    /// <summary>绑定应用宿主。</summary>
    /// <param name="host">提供捕获控制器的宿主。</param>
    public GestureInvoker(AppHost host) => _host = host;

    /// <inheritdoc/>
    public void BeginBinding(GC.GestureBinding binding) { }

    /// <inheritdoc/>
    public bool InvokeChain(GC.GestureBinding binding)
    {
        if (binding.Actions.Count == 0) return false;
        var dto = _host.Settings.Current.Gestures.Bindings.FirstOrDefault(b => b.Id == binding.Id
            && string.Equals(b.Trigger, binding.Trigger.ToString(), StringComparison.OrdinalIgnoreCase)
            && b.ToBinding()?.Chord.Equals(binding.Chord) == true);
        if (dto is { EntryId.Length: > 0 })
        {
            var config = _host.Settings.Current.Gestures;
            string entry = dto.EntryId, identity = dto.Id;
            Post(() => _host.Controller.BeginBehavior(config.BindingGraph(dto), entry, identity));
            return true;
        }
        var chain = binding.Actions.Concat(binding.OnComplete).Select(GestureActions.Canonical).ToArray();
        var failures = binding.OnFail.ToArray();
        Post(() => _host.Controller.BeginChain(chain, failures, binding.Fallback));
        return true;
    }

    /// <summary>把单步动作作为动作链投递。</summary>
    /// <param name="actionName">动作名。</param>
    /// <returns>动作名是否可识别。</returns>
    public bool Invoke(string actionName)
    {
        if (!GestureActions.IsValid(actionName)) return false;
        Post(() => _host.Controller.BeginChain(new[] { GestureActions.Canonical(actionName) }, Array.Empty<string>()));
        return true;
    }

    static void Post(Action action)
    {
        var app = System.Windows.Application.Current;
        if (app == null) { action(); return; }
        app.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Send, action);
    }
}
