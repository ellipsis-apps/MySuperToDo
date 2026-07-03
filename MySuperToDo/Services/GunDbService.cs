using System.Text.Json;

using Microsoft.Extensions.Configuration;
using Microsoft.JSInterop;

using MySuperToDo.Application.Interfaces;

namespace MySuperToDo.Services;

/// <summary>
/// JSInterop wrapper around GunDB with a reticle (scoped root node) for app-level data isolation.
///
/// Configuration:
///   GunDB:AppScope  — root key for the reticle, defaults to "mysupertodo"
///
/// The JS module and Gun instance are initialised lazily on the first call,
/// so no explicit InitializeAsync step is required in consuming components.
/// </summary>
internal sealed class GunDbService : IGunDbService, IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private readonly string _appScope;
    private readonly object _gate = new();
    private string[] _peers = [];

    public bool HasPeers => _peers.Length > 0;
    public IReadOnlyList<string> PeerUrls => _peers;
    public event Action<IReadOnlyList<string>>? PeersChanged;

    private IJSObjectReference? _module;
    private bool _initialized;
    private bool _disposed;

    private readonly Dictionary<string, GunCallbackProxy> _callbacks = new();
    private readonly Dictionary<string, GunCallbackProxy> _mapCallbacks = new();

    public GunDbService(IJSRuntime js, IConfiguration configuration)
    {
        _js = js;
        _appScope = configuration["GunDB:AppScope"] ?? "mysupertodo";
        _peers = configuration.GetSection("GunDB:MyPeers").Get<string[]>() ?? [];
    }

    private async ValueTask<IJSObjectReference> GetModuleAsync(CancellationToken cancellationToken = default)
    {
        _module ??= await _js.InvokeAsync<IJSObjectReference>(
            "import", cancellationToken, "./js/gun-interop.js");

        if (!_initialized)
        {
            await _module.InvokeVoidAsync("initialize", cancellationToken, _peers, _appScope);
            _initialized = true;
        }

        return _module;
    }

    public async Task UpdatePeersAsync(IEnumerable<string> peerUrls, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(peerUrls);

        var nextPeers = peerUrls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        lock (_gate)
        {
            _peers = nextPeers;
        }

        PeersChanged?.Invoke(PeerUrls);

        var module = await GetModuleAsync(cancellationToken);

        // Log peer updates for diagnostics
        var peerList = _peers.Length > 0 ? string.Join(", ", _peers) : "(none)";
        System.Diagnostics.Debug.WriteLine($"[GunDB] UpdatePeersAsync: Peers = {peerList}");

        await module.InvokeVoidAsync("reinitialize", cancellationToken, _peers, _appScope);
    }

    public async Task PutAsync(string path, object data, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var module = await GetModuleAsync(cancellationToken);
        System.Diagnostics.Debug.WriteLine($"[GunDB] PutAsync: {path}");
        await module.InvokeAsync<bool>("putAsync", cancellationToken, path, JsonSerializer.Serialize(data));
    }

    public async Task SetAsync(string path, object data, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var module = await GetModuleAsync(cancellationToken);
        System.Diagnostics.Debug.WriteLine($"[GunDB] SetAsync: {path}");
        await module.InvokeAsync<bool>("setAsync", cancellationToken, path, JsonSerializer.Serialize(data));
    }

    public async Task<T?> GetOnceAsync<T>(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var module = await GetModuleAsync(cancellationToken);
        var json = await module.InvokeAsync<string?>("getOnceAsync", cancellationToken, path);
        return json is null ? default : JsonSerializer.Deserialize<T>(json);
    }

    public async Task SubscribeAsync(string path, Func<string, string, Task> onData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(onData);

        var module = await GetModuleAsync();

        if (_callbacks.TryGetValue(path, out var existing))
        {
            // Stop JS callbacks first, then dispose DotNetObjectReference to avoid
            // "There is no tracked object" races.
            await module.InvokeVoidAsync("unsubscribe", path);
            existing.Dispose();
            _callbacks.Remove(path);
        }

        var proxy = new GunCallbackProxy(onData);
        _callbacks[path] = proxy;

        await module.InvokeVoidAsync("subscribe", path, proxy.DotNetRef, nameof(GunCallbackProxy.OnDataAsync));
    }

    public async Task UnsubscribeAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var module = await GetModuleAsync();
        await module.InvokeVoidAsync("unsubscribe", path);

        if (_callbacks.Remove(path, out var proxy))
            proxy.Dispose();
    }

    public async Task<IAsyncDisposable> SubscribeMapAsync(string path, Func<string, string, Task> onItem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(onItem);

        var module = await GetModuleAsync();
        var subscriptionId = Guid.NewGuid().ToString();
        var proxy = new GunCallbackProxy(onItem);
        _mapCallbacks[subscriptionId] = proxy;

        await module.InvokeVoidAsync(
            "subscribeMap", subscriptionId, path, proxy.DotNetRef, nameof(GunCallbackProxy.OnDataAsync));

        return new MapSubscriptionToken(subscriptionId, this);
    }

    internal async Task RemoveMapCallbackAsync(string subscriptionId)
    {
        if (!_mapCallbacks.Remove(subscriptionId, out var proxy)) return;

        if (_module is not null)
        {
            try { await _module.InvokeVoidAsync("unsubscribeMap", subscriptionId); }
            catch (JSDisconnectedException) { }
        }

        proxy.Dispose();
    }

    public async Task RemoveAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var module = await GetModuleAsync(cancellationToken);
        await module.InvokeAsync<bool>("removeAsync", cancellationToken, path);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_module is not null)
        {
            try
            {
                await _module.InvokeVoidAsync("disposeAll");
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException) { }
        }

        foreach (var proxy in _callbacks.Values)
            proxy.Dispose();
        _callbacks.Clear();

        foreach (var proxy in _mapCallbacks.Values)
            proxy.Dispose();
        _mapCallbacks.Clear();
    }

    // ── Inner types ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returned by <see cref="SubscribeMapAsync"/>. Disposing removes this subscriber's
    /// JS subscription and releases its DotNetObjectReference.
    /// </summary>
    private sealed class MapSubscriptionToken(
        string subscriptionId,
        GunDbService service) : IAsyncDisposable
    {
        private bool _disposed;

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            await service.RemoveMapCallbackAsync(subscriptionId);
        }
    }
}
