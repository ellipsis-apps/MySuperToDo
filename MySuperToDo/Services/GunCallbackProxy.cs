using Microsoft.JSInterop;

namespace MySuperToDo.Services;

/// <summary>
/// Bridges a GunDB .on() subscription to a managed Func delegate.
/// Owns the DotNetObjectReference so the JS side can invoke back into .NET.
/// </summary>
internal sealed class GunCallbackProxy : IDisposable
{
    private readonly Func<string, string, Task> _callback;

    public DotNetObjectReference<GunCallbackProxy> DotNetRef { get; }

    public GunCallbackProxy(Func<string, string, Task> callback)
    {
        _callback = callback;
        DotNetRef = DotNetObjectReference.Create(this);
    }

    [JSInvokable]
    public Task OnDataAsync(string json, string soul) => _callback(json, soul);

    public void Dispose() => DotNetRef.Dispose();
}
