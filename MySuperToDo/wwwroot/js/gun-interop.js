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
 * @param {string}   seed - Optional seed for deterministic user identity.
 */
export function initialize(peers, appScope, seed) {
    _disposed = false;
    const opts = {};
    if (peers && peers.length > 0) {
        opts.peers = peers;
    }

    // Initialize Gun with IndexedDB (default storage)
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

// ============================================================================
// Encrypted Seed Storage in IndexedDB
// ============================================================================

const SEED_STORE_NAME = 'gundb-seed';
const SEED_DB_NAME = 'MySuperToDo';
const SEED_KEY = 'encrypted-seed';

/**
 * Derives a key from a password using PBKDF2.
 * @param {string} password - User password
 * @returns {Promise<CryptoKey>} Derived encryption key
 */
async function deriveKeyFromPassword(password) {
    const encoder = new TextEncoder();
    const data = encoder.encode(password);

    const baseKey = await crypto.subtle.importKey(
        'raw',
        data,
        'PBKDF2',
        false,
        ['deriveBits', 'deriveKey']
    );

    return crypto.subtle.deriveKey(
        {
            name: 'PBKDF2',
            salt: encoder.encode('MySuperToDo-salt'), // Fixed salt; in production use random per-seed
            iterations: 100000,
            hash: 'SHA-256',
        },
        baseKey,
        { name: 'AES-GCM', length: 256 },
        false,
        ['encrypt', 'decrypt']
    );
}

/**
 * Encrypts a seed with a password using AES-GCM.
 * @param {string} seed - The seed to encrypt
 * @param {string} password - User password
 * @returns {Promise<string>} Base64-encoded encrypted data with IV
 */
export async function encryptSeed(seed, password) {
    const key = await deriveKeyFromPassword(password);
    const iv = crypto.getRandomValues(new Uint8Array(12)); // 96-bit IV for GCM
    const encoder = new TextEncoder();
    const data = encoder.encode(seed);

    const encrypted = await crypto.subtle.encrypt(
        { name: 'AES-GCM', iv },
        key,
        data
    );

    // Combine IV + encrypted data and return as base64
    const combined = new Uint8Array(iv.length + encrypted.byteLength);
    combined.set(iv, 0);
    combined.set(new Uint8Array(encrypted), iv.length);

    return btoa(String.fromCharCode.apply(null, combined));
}

/**
 * Decrypts encrypted seed with a password.
 * @param {string} encryptedData - Base64-encoded encrypted data with IV
 * @param {string} password - User password
 * @returns {Promise<string>} Decrypted seed
 * @throws {Error} If decryption fails (invalid password, corrupted data)
 */
export async function decryptSeed(encryptedData, password) {
    try {
        const key = await deriveKeyFromPassword(password);
        const binary = atob(encryptedData);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) {
            bytes[i] = binary.charCodeAt(i);
        }

        const iv = bytes.slice(0, 12);
        const encrypted = bytes.slice(12);

        const decrypted = await crypto.subtle.decrypt(
            { name: 'AES-GCM', iv },
            key,
            encrypted
        );

        const decoder = new TextDecoder();
        return decoder.decode(decrypted);
    } catch (err) {
        throw new Error('Failed to decrypt seed: invalid password or corrupted data');
    }
}

/**
 * Opens the IndexedDB for seed storage.
 * @returns {Promise<IDBDatabase>}
 */
async function openSeedDatabase() {
    return new Promise((resolve, reject) => {
        const request = indexedDB.open(SEED_DB_NAME, 1);

        request.onerror = () => reject(request.error);
        request.onsuccess = () => resolve(request.result);

        request.onupgradeneeded = (event) => {
            const db = event.target.result;
            if (!db.objectStoreNames.contains(SEED_STORE_NAME)) {
                db.createObjectStore(SEED_STORE_NAME);
            }
        };
    });
}

/**
 * Stores encrypted seed in IndexedDB.
 * @param {string} encryptedSeed - Base64-encoded encrypted seed
 * @returns {Promise<void>}
 */
export async function storeSeedInIndexedDB(encryptedSeed) {
    const db = await openSeedDatabase();
    return new Promise((resolve, reject) => {
        const tx = db.transaction(SEED_STORE_NAME, 'readwrite');
        const store = tx.objectStore(SEED_STORE_NAME);
        const request = store.put(encryptedSeed, SEED_KEY);

        request.onerror = () => reject(request.error);
        tx.oncomplete = () => resolve();
    });
}

/**
 * Retrieves encrypted seed from IndexedDB.
 * @returns {Promise<string|null>} Base64-encoded encrypted seed, or null if not found
 */
export async function getSeedFromIndexedDB() {
    const db = await openSeedDatabase();
    return new Promise((resolve, reject) => {
        const tx = db.transaction(SEED_STORE_NAME, 'readonly');
        const store = tx.objectStore(SEED_STORE_NAME);
        const request = store.get(SEED_KEY);

        request.onerror = () => reject(request.error);
        request.onsuccess = () => resolve(request.result || null);
    });
}

/**
 * Removes encrypted seed from IndexedDB.
 * @returns {Promise<void>}
 */
export async function deleteSeedFromIndexedDB() {
    const db = await openSeedDatabase();
    return new Promise((resolve, reject) => {
        const tx = db.transaction(SEED_STORE_NAME, 'readwrite');
        const store = tx.objectStore(SEED_STORE_NAME);
        const request = store.delete(SEED_KEY);

        request.onerror = () => reject(request.error);
        tx.oncomplete = () => resolve();
    });
}
