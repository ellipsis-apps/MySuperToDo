namespace MySuperToDo.Application.Interfaces;

/// <summary>
/// GunDB operations scoped to the app's reticle (root namespace node).
/// All paths are relative to the reticle configured via GunDB:AppScope.
/// </summary>
public interface IGunDbService
{
    /// <summary>Gets whether GunDB is configured with at least one peer.</summary>
    bool HasPeers { get; }

    /// <summary>Merges data at <paramref name="path"/> under the reticle.</summary>
    Task PutAsync(string path, object data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds <paramref name="data"/> to an unordered set at <paramref name="path"/> under the reticle.
    /// Use for collections where each entry needs its own generated soul.
    /// </summary>
    Task SetAsync(string path, object data, CancellationToken cancellationToken = default);

    /// <summary>Returns the current value at <paramref name="path"/> (one-shot read).</summary>
    Task<T?> GetOnceAsync<T>(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to live changes at <paramref name="path"/>.
    /// <paramref name="onData"/> receives <c>(json, soul)</c> on every change.
    /// Replaces any previous subscription at the same path.
    /// </summary>
    Task SubscribeAsync(string path, Func<string, string, Task> onData);

    /// <summary>Cancels the live subscription at <paramref name="path"/>.</summary>
    Task UnsubscribeAsync(string path);

    /// <summary>
    /// Subscribes to every item in a collection at <paramref name="path"/> via GunDB <c>.map().on()</c>.
    /// <paramref name="onItem"/> receives <c>(json, soul)</c> once per item and again on each change.
    /// Use this to enumerate all children stored under a parent path.
    /// Dispose the returned token to cancel just this subscription without affecting other subscribers.
    /// </summary>
    Task<IAsyncDisposable> SubscribeMapAsync(string path, Func<string, string, Task> onItem);

    /// <summary>Removes (nulls out) the node at <paramref name="path"/>.</summary>
    Task RemoveAsync(string path, CancellationToken cancellationToken = default);
}
