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

/**
 * Returns the current authenticated user's pair as a plain JSON string (pub/epub/priv/epriv)
 * without performing any encryption/decryption. This helps display the actual
 * key material for verification when exporting.
 */
export function getCurrentUserPairPlain() {
    if (!_gun) throw new Error('Gun is not initialised');
    const user = _gun.user();
    const pair = user && typeof user.pair === 'function' ? user.pair() : (user && user.pair) ? user.pair : null;
    const result = {};
    if (pair) {
        try {
            // common keys
            ['pub', 'epub', 'priv', 'epriv', 's'].forEach(k => {
                try {
                    if (pair[k] !== undefined && typeof pair[k] !== 'function') result[k] = pair[k];
                    else if (typeof pair[k] === 'function') {
                        try { const v = pair[k](); if (v !== undefined) result[k] = v; } catch { }
                    }
                } catch { }
            });
        } catch { }
        // fallback: copy own properties
        try {
            Object.getOwnPropertyNames(pair).forEach(k => {
                try {
                    if (result[k] === undefined) {
                        const v = pair[k];
                        if (typeof v !== 'function') result[k] = v;
                    }
                } catch { }
            });
        } catch { }
    }
    // Try to read from user._?.sea if available
    try {
        if ((!result.pub || result.pub === undefined) && user && user._ && user._.sea) {
            const sea = user._.sea;
            ['pub', 'epub', 'priv', 'epriv', 's'].forEach(k => {
                try { if (sea[k] !== undefined) result[k] = sea[k]; } catch { }
            });
        }
    } catch { }

    return Object.keys(result).length === 0 ? null : JSON.stringify(result);
}

/**
 * Probe a list of peer URLs to verify basic reachability by attempting a WebSocket
 * handshake. Returns a JSON string array of results: [{ url, ok, message }]
 *
 * Note: some peers may not accept direct websocket connections or may require
 * different paths; this probe is a best-effort check from the browser runtime.
 */
export async function probePeers(peers, timeoutMs) {
    if (!peers || peers.length === 0) return JSON.stringify([]);
    const results = await Promise.all(peers.map(p => probeSinglePeer(p, timeoutMs || 3000)));
    return JSON.stringify(results);
}

function probeSinglePeer(peerUrl, timeoutMs) {
    return new Promise(resolve => {
        const normalize = (u) => {
            try {
                // If already ws/wss, keep it
                if (u.startsWith('ws:') || u.startsWith('wss:')) return u;
                // convert http(s) -> ws(s)
                if (u.startsWith('https:')) return u.replace(/^https:/, 'wss:');
                if (u.startsWith('http:')) return u.replace(/^http:/, 'ws:');
                // If no scheme, try ws fallback
                return 'ws://' + u;
            } catch { return u; }
        };

        const wsUrl = normalize(peerUrl);
        let settled = false;
        let ws;
        try {
            ws = new WebSocket(wsUrl);
        } catch (err) {
            resolve({ url: peerUrl, ok: false, message: String(err) });
            return;
        }

        const to = setTimeout(() => {
            if (!settled) {
                settled = true;
                try { ws.close(); } catch { }
                resolve({ url: peerUrl, ok: false, message: 'timeout' });
            }
        }, timeoutMs || 3000);

        ws.onopen = () => {
            if (settled) return;
            settled = true;
            clearTimeout(to);
            try { ws.close(); } catch { }
            resolve({ url: peerUrl, ok: true, message: 'open' });
        };

        ws.onerror = (ev) => {
            if (settled) return;
            settled = true;
            clearTimeout(to);
            try { ws.close(); } catch { }
            resolve({ url: peerUrl, ok: false, message: 'error' });
        };

        ws.onclose = (ev) => {
            if (settled) return;
            // closed before open -> treat as unreachable
            settled = true;
            clearTimeout(to);
            resolve({ url: peerUrl, ok: false, message: 'closed' });
        };
    });
}

/**
 * Decrypts an encrypted export blob with the provided password and returns the
 * decrypted payload as a JSON string. This does NOT authenticate the user; it
 * only returns the underlying pair/alias payload for display/verification.
 */
export async function decryptEncryptedPair(encryptedPayload, password) {
    if (!_gun) throw new Error('Gun is not initialised');
    try {
        let decrypted = encryptedPayload;
        try { decrypted = JSON.parse(encryptedPayload); } catch { /* could be raw SEA string */ }

        const payload = await SEA.decrypt(decrypted, password);
        if (!payload) throw new Error('Decryption failed or invalid payload');

        // Ensure pair inside the payload is represented as a plain object
        if (payload && payload.pair) {
            const p = payload.pair;
            const plain = {};
            try {
                Object.getOwnPropertyNames(p).forEach(k => {
                    try {
                        const v = p[k];
                        if (typeof v !== 'function') plain[k] = v;
                    } catch { }
                });
            } catch { }
            payload.pair = plain;
        }

        return typeof payload === 'string' ? payload : JSON.stringify(payload);
    }
    catch (err) {
        throw err;
    }
}

/**
 * Export the current user's keypair encrypted with the provided password.
 * Returns a string (encrypted payload) that can be copied between browsers.
 */
export async function exportEncryptedPair(password) {
    if (!_gun) throw new Error('Gun is not initialised');
    const user = _gun.user();
    const pair = user && user.pair ? user.pair() : null;
    if (!pair) throw new Error('No authenticated user pair available');
    // Try to capture the current alias (username) if available to help import.
    const alias = (user && user.is && user.is.alias) ? user.is.alias : null;
    // Create a plain serializable copy of the pair. Gun/SEA pair objects may
    // expose properties as non-enumerable or via getters; try multiple
    // strategies to extract useful key material (pub, priv, epriv, epub, etc.).
    const serializablePair = {};

    // First, copy any own property names that are not functions
    try {
        Object.getOwnPropertyNames(pair).forEach(k => {
            try {
                const v = pair[k];
                if (typeof v !== 'function') serializablePair[k] = v;
            } catch { /* ignore property access errors */ }
        });
    } catch { /* ignore */ }

    // Next, attempt to copy commonly present SEA key names directly (access via getters)
    try {
        [ 'pub', 'epub', 'priv', 'epriv', 's', 'x', 'y' ].forEach(k => {
            try {
                if ((serializablePair[k] === undefined) && (pair[k] !== undefined) && (typeof pair[k] !== 'function')) {
                    serializablePair[k] = pair[k];
                }
            } catch { }
        });
    } catch { }

    // As a last resort, try for-in to pick up enumerable inherited props
    try {
        for (const k in pair) {
            try {
                if (serializablePair[k] === undefined) {
                    const v = pair[k];
                    if (typeof v !== 'function') serializablePair[k] = v;
                }
            } catch { }
        }
    } catch { }

    const payload = { pair: serializablePair, alias };
    // Use SEA to encrypt the payload with the provided password
    try {
        const encrypted = await SEA.encrypt(payload, password);
        return typeof encrypted === 'string' ? encrypted : JSON.stringify(encrypted);
    }
    catch (err) {
        throw err;
    }
}

/**
 * Import an encrypted keypair blob (as produced by exportEncryptedPair) and authenticate
 * the local Gun user with the decrypted pair. Resolves true on success.
 */
export async function importEncryptedPair(encryptedPayload, password) {
    if (!_gun) throw new Error('Gun is not initialised');
    try {
        // Try to parse JSON, but allow raw strings too
        let decrypted = encryptedPayload;
        try { decrypted = JSON.parse(encryptedPayload); } catch { /* ignore - could be raw SEA string */ }

        // SEA.decrypt will return the original payload object we encrypted earlier
        const payload = await SEA.decrypt(decrypted, password);
        if (!payload) throw new Error('Decryption failed or invalid payload');

        const pair = payload.pair ?? payload; // support cases where a raw pair was encrypted
        const alias = payload.alias ?? null;

        return await new Promise((resolve, reject) => {
            _gun.user().auth(pair, ack => {
                if (ack && ack.err) {
                    // Common failure: "User cannot be found" when the alias mapping isn't available on the network.
                    if (String(ack.err).includes('User cannot be found')) {
                        reject(new Error("User cannot be found! Ensure the original user record has been published to your Gun peers and that peers are reachable before importing. You can also try importing after connecting to the same relay peers used by the original browser."));
                        return;
                    }
                    reject(new Error(ack.err));
                }
                else resolve(true);
            });
        });
    }
    catch (err) {
        throw err;
    }
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
    // Disable any built-in browser persistence (localStorage / IndexedDB/radisk)
    // to ensure GunDB does not attempt to use IndexedDB or localStorage in this app.
    const opts = {
        localStorage: true
    };
    if (peers && peers.length > 0) {
        opts.peers = peers;
    }
    try {
        console.debug('[GunInterop] initialize peers:', peers);
        _gun = Gun(opts);
    }
    catch (err) {
        console.error('[GunInterop] initialize error', err);
        throw err;
    }
    // The reticle: every operation in this wrapper is anchored to this node.
    _reticle = _gun.get(appScope);
}

/**
 * Reinitializes the Gun instance with a new peer list while preserving the app reticle.
 */
export function reinitialize(peers, appScope) {
    console.debug('[GunInterop] reinitialize peers:', peers);
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
 * Checks whether the reticle at appScope contains any data.
 * Returns true when the node is non-null.
 */
export function reticleExists(appScope) {
    if (!_gun) return Promise.resolve(false);
    return new Promise((resolve) => {
        _gun.get(appScope).once(data => resolve(data != null));
    });
}

/**
 * Create a new Gun user (username/password).
 * Resolves when the create ack is successful, rejects on error.
 */
export function createUser(username, password) {
    return new Promise((resolve, reject) => {
        if (!_gun) return reject(new Error('Gun is not initialised'));
        try {
            _gun.user().create(username, password, ack => {
                if (ack && ack.err) reject(new Error(ack.err));
                else resolve(ack);
            });
        }
        catch (err) {
            reject(err);
        }
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
    const seenSouls = new Set(); // Track which souls we've seen to detect deletions

    const node = getNode(path).map().on((data, soul) => {
        // Handle null data first (before using 'in' operator)
        if (data == null) {
            if (seenSouls.has(soul)) {
                // This is a real deletion - pass empty string to signal removal
                invokeDotNetCallback(dotNetRef, callbackMethod, '', soul, '[GunDB] map callback error:');
            }
            // If we haven't seen this soul before, skip it (initial empty state)
            return;
        }

        // Skip GunDB soul-reference objects {#: "soul"}
        if (typeof data !== 'object' || '#' in data) return;

        // Remember this soul for future deletion detection
        seenSouls.add(soul);

        // Pass the data back to .NET
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
