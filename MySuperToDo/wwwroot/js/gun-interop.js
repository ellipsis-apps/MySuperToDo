// GunDB JSInterop wrapper for Blazor WASM.
// Gun is loaded as a global from the <script> tag in index.html.
//
// The "reticle" is the app-scoped root node (gun.get(appScope)) that namespaces
// all of this app's data within the GunDB graph, keeping it isolated from other
// apps sharing the same Gun peer network.

let _gun = null;
let _reticle = null;
let _disposed = false;

const SEED_STORAGE_KEY = 'mysupertodo.encrypted_seed';
const GUN_STORAGE_PREFIX = 'gun';

function getLocalStorage() {
    try {
        return window.localStorage;
    } catch (err) {
        console.warn('[GunDB] localStorage unavailable:', err);
        return null;
    }
}

function getGunStorageKeys(storage) {
    if (!storage) {
        return [];
    }

    const keys = [];
    for (let i = 0; i < storage.length; i++) {
        const key = storage.key(i);
        if (!key) {
            continue;
        }

        if (key === SEED_STORAGE_KEY || key === GUN_STORAGE_PREFIX || key.startsWith(`${GUN_STORAGE_PREFIX}/`) || key.startsWith(`${GUN_STORAGE_PREFIX}-`)) {
            keys.push(key);
        }
    }

    return keys;
}

async function deriveKey(password) {
    const encoder = new TextEncoder();
    const keyMaterial = await crypto.subtle.importKey(
        'raw',
        encoder.encode(password),
        { name: 'PBKDF2' },
        false,
        ['deriveKey']
    );

    return crypto.subtle.deriveKey(
        {
            name: 'PBKDF2',
            salt: encoder.encode('mysupertodo-salt'),
            iterations: 100000,
            hash: 'SHA-256'
        },
        keyMaterial,
        { name: 'AES-GCM', length: 256 },
        false,
        ['encrypt', 'decrypt']
    );
}

export async function encryptSeed(seed, password) {
    const encoder = new TextEncoder();
    const key = await deriveKey(password);
    const iv = crypto.getRandomValues(new Uint8Array(12));
    const ciphertext = await crypto.subtle.encrypt({ name: 'AES-GCM', iv }, key, encoder.encode(seed));

    const payload = new Uint8Array(iv.byteLength + ciphertext.byteLength);
    payload.set(iv, 0);
    payload.set(new Uint8Array(ciphertext), iv.byteLength);

    return btoa(String.fromCharCode(...payload));
}

export async function decryptSeed(encryptedSeed, password) {
    const data = Uint8Array.from(atob(encryptedSeed), c => c.charCodeAt(0));
    const iv = data.slice(0, 12);
    const ciphertext = data.slice(12);
    const key = await deriveKey(password);

    const plaintext = await crypto.subtle.decrypt({ name: 'AES-GCM', iv }, key, ciphertext);
    return new TextDecoder().decode(plaintext);
}

export function storeSeedInIndexedDB(encryptedSeed) {
    const storage = getLocalStorage();
    if (!storage) {
        throw new Error('localStorage is unavailable');
    }

    storage.setItem(SEED_STORAGE_KEY, encryptedSeed);
    return true;
}

export function getSeedFromIndexedDB() {
    const storage = getLocalStorage();
    if (!storage) {
        return null;
    }

    return storage.getItem(SEED_STORAGE_KEY);
}

export function deleteSeedFromIndexedDB() {
    const storage = getLocalStorage();
    if (!storage) {
        return;
    }

    storage.removeItem(SEED_STORAGE_KEY);
    getGunStorageKeys(storage).forEach(key => storage.removeItem(key));
}

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
 * Checks whether the GunDB localStorage data exists locally.
 */
export function gunDatabaseExists() {
    const storage = getLocalStorage();
    if (!storage) {
        return false;
    }

    return getGunStorageKeys(storage).length > 0 || storage.getItem(SEED_STORAGE_KEY) != null;
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
 * @param {string}   seed - Optional seed for deterministic user identity.
 */
export function initialize(peers, appScope, seed) {
    _disposed = false;
    const opts = {
        localStorage: true
    };
    if (peers && peers.length > 0) {
        opts.peers = peers;
    }

    // Initialize Gun with localStorage persistence so the app data survives refreshes.
    _gun = Gun(opts);
    _reticle = _gun.get(appScope);

    // If a seed is provided, authenticate with a deterministic username/password
    // Gun derives keys from the username and password combination using bcrypt,
    // so the same seed always produces the same Gun identity (same public/private keys).
    if (seed && seed.length > 0) {
        var gunUser = _gun.user();
        // Use fixed username with seed as password
        // This ensures the same seed always produces the same Gun keys
        gunUser.auth('seed_user', seed, function(ack) {
            if (ack.err) {
                console.log('[GunDB] Auth error (may be expected on first use):', ack.err);
                // On first use, we need to create the account
                gunUser.create('seed_user', seed, function(ack2) {
                    if (ack2.err) {
                        console.log('[GunDB] Create error:', ack2.err);
                    } else {
                        console.log('[GunDB] User created and authenticated with seed');
                    }
                });
            } else {
                console.log('[GunDB] User authenticated with seed');
            }
        });
    }

    // Signal that initialization is complete
    console.log('[GunDB] initialized with appScope:', appScope, 'seed provided:', !!seed);
}

/**
 * Reinitializes the Gun instance with a new peer list while preserving the app reticle.
 * @param {string[]} peers - Optional relay peer URLs.
 * @param {string}   appScope - Root key used as the reticle.
 * @param {string}   seed - Optional seed for deterministic user identity.
 */
export function reinitialize(peers, appScope, seed) {
    disposeAll();
    initialize(peers, appScope, seed);
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

