// GunDB JSInterop wrapper for Blazor WASM.
// Gun is loaded as a global from the <script> tag in index.html.
//
// The "reticle" is the app-scoped root node (gun.get(appScope)) that namespaces
// all of this app's data within the GunDB graph, keeping it isolated from other
// apps sharing the same Gun peer network.

let _gun = null;
let _reticle = null;
let _disposed = false;

// Maps path -> { node, dotNetRef } for active .on() subscriptions
const _subscriptions = new Map();

// Maps path -> { node, dotNetRef } for active .map().on() collection subscriptions
const _mapSubscriptions = new Map();

/**
 * Traverses the reticle by splitting path on '/' and chaining .get() calls.
 * "lists/abc123" becomes reticle.get("lists").get("abc123").
 */
function getNode(path) {
    return path.split('/').reduce((node, part) => node.get(part), _reticle);
}

function invokeDotNetCallback(dotNetRef, callbackMethod, json, soul, errorLabel) {
    if (_disposed) {
        return;
    }

    try {
        dotNetRef.invokeMethodAsync(callbackMethod, json, soul)
            .catch(err => {
                const message = String(err ?? '');
                if (message.includes('There is no tracked object') || message.includes('already disposed')) {
                    return;
                }
                console.error(errorLabel, err);
            });
    }
    catch (err) {
        const message = String(err ?? '');
        if (message.includes('There is no tracked object') || message.includes('already disposed')) {
            return;
        }
        console.error(errorLabel, err);
    }
}

/**
 * Initialise the Gun instance and pin the reticle.
 * @param {string[]} peers - Optional relay peer URLs.
 * @param {string}   appScope - Root key used as the reticle (e.g. "mysupertodo").
 */
export function initialize(peers, appScope) {
    _disposed = false;
    const opts = {};
    if (peers && peers.length > 0) {
        opts.peers = peers;
    }
    _gun = Gun(opts);
    // The reticle: every operation in this wrapper is anchored to this node.
    _reticle = _gun.get(appScope);
}

/**
 * Reinitializes the Gun instance with a new peer list while preserving the app reticle.
 */
export function reinitialize(peers, appScope) {
    disposeAll();
    initialize(peers, appScope);
}

/**
 * Merges (put) a JSON-serialised object at the nested path under the reticle.
 */
export function putAsync(path, jsonData) {
    return new Promise((resolve, reject) => {
        getNode(path).put(JSON.parse(jsonData), ({ err }) => {
            if (err) reject(new Error(err));
            else resolve(true);
        });
    });
}

/**
 * Adds an item to an unordered set (set) at the nested path.
 * Use this for collections; each item gets its own generated soul.
 */
export function setAsync(path, jsonData) {
    return new Promise((resolve, reject) => {
        getNode(path).set(JSON.parse(jsonData), ({ err }) => {
            if (err) reject(new Error(err));
            else resolve(true);
        });
    });
}

/**
 * One-shot read of the nested path. Returns null when the node is empty.
 */
export function getOnceAsync(path) {
    return new Promise((resolve) => {
        getNode(path).once(data =>
            resolve(data != null ? JSON.stringify(data) : null)
        );
    });
}

/**
 * Subscribes to live changes at the nested path.
 * Invokes dotNetRef[callbackMethod](json, soul) on every change.
 * Replaces any existing subscription at the same path.
 */
export function subscribe(path, dotNetRef, callbackMethod) {
    unsubscribe(path);

    const node = getNode(path).on((data, soul) => {
        if (data != null) {
            invokeDotNetCallback(dotNetRef, callbackMethod, JSON.stringify(data), soul, '[GunDB] callback error:');
        }
    });

    _subscriptions.set(path, { node, dotNetRef });
}

/**
 * Cancels the live subscription at the given path.
 */
export function unsubscribe(path) {
    const entry = _subscriptions.get(path);
    if (entry) {
        entry.node.off();
        _subscriptions.delete(path);
    }
}

/**
 * Subscribes to every item in a collection via .map().on().
 * Uses a unique subscriptionId so multiple independent subscribers can coexist on
 * the same path — each gets its own .map().on() and therefore its own initial replay.
 */
export function subscribeMap(subscriptionId, path, dotNetRef, callbackMethod) {
    const node = getNode(path).map().on((data, soul) => {
        // Skip null, non-objects, and GunDB soul-reference objects {#: "soul"}
        if (data == null || typeof data !== 'object' || '#' in data) return;

        invokeDotNetCallback(dotNetRef, callbackMethod, JSON.stringify(data), soul, '[GunDB] map callback error:');
    });

    _mapSubscriptions.set(subscriptionId, { node, dotNetRef });
}

/**
 * Cancels the map subscription identified by subscriptionId.
 */
export function unsubscribeMap(subscriptionId) {
    const entry = _mapSubscriptions.get(subscriptionId);
    if (entry) {
        entry.node.off();
        _mapSubscriptions.delete(subscriptionId);
    }
}

/**
 * Removes a node by writing null, effectively deleting it from the graph.
 */
export function removeAsync(path) {
    return new Promise((resolve, reject) => {
        getNode(path).put(null, ({ err }) => {
            if (err) reject(new Error(err));
            else resolve(true);
        });
    });
}

/**
 * Tears down all subscriptions and releases the Gun instance.
 * Called by GunDbService.DisposeAsync().
 */
export function disposeAll() {
    _disposed = true;

    for (const [, entry] of _subscriptions) {
        entry.node.off();
    }
    _subscriptions.clear();

    for (const [, entry] of _mapSubscriptions) {
        entry.node.off();
    }
    _mapSubscriptions.clear();

    _gun = null;
    _reticle = null;
}
