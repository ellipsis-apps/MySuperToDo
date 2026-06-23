using Microsoft.JSInterop;
using System.Security.Cryptography;

namespace MySuperToDo.Services;

/// <summary>
/// Service for managing GunDB seed generation, storage, and deletion.
/// A seed is a cryptographic value used to regenerate GunDB identity key pairs.
/// Seeds are encrypted and persisted in IndexedDB for secure app initialization.
/// </summary>
public sealed class GunDbSeedService
{
    private const int SeedLength = 32; // 256 bits

    private readonly IJSRuntime _jsRuntime;
    private readonly IJSObjectReference? _gunInterop;

    public GunDbSeedService(IJSRuntime jsRuntime, IJSObjectReference? gunInterop = null)
    {
        _jsRuntime = jsRuntime;
        _gunInterop = gunInterop;
    }

    /// <summary>
    /// Gets the encrypted seed from IndexedDB and decrypts it with the provided password.
    /// </summary>
    /// <param name="password">User password for decryption.</param>
    /// <returns>The decrypted seed string, or null if no seed is stored.</returns>
    /// <exception cref="InvalidOperationException">If decryption fails.</exception>
    public async Task<string?> GetStoredSeedAsync(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        try
        {
            var gunInterop = await GetGunInteropAsync();
            var encryptedSeed = await gunInterop.InvokeAsync<string?>("getSeedFromIndexedDB");

            if (string.IsNullOrEmpty(encryptedSeed))
            {
                return null;
            }

            // Decrypt the seed with the provided password
            try
            {
                var decrypted = await gunInterop.InvokeAsync<string>("decryptSeed", encryptedSeed, password);
                return decrypted;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GunDbSeedService] Decryption failed: {ex.Message}");
                throw new InvalidOperationException("Invalid password or corrupted seed data", ex);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GunDbSeedService] Failed to retrieve seed: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Checks if an encrypted GunDB seed is stored in IndexedDB.
    /// </summary>
    /// <returns>True if a seed exists, false otherwise.</returns>
    public async Task<bool> SeedExistsAsync()
    {
        try
        {
            var gunInterop = await GetGunInteropAsync();
            var encryptedSeed = await gunInterop.InvokeAsync<string?>("getSeedFromIndexedDB");
            return !string.IsNullOrWhiteSpace(encryptedSeed);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GunDbSeedService] Failed to check seed existence: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Generates a new random seed for GunDB.
    /// </summary>
    /// <returns>A base64-encoded seed string.</returns>
    public string GenerateNewSeed()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(SeedLength);
        return Convert.ToBase64String(randomBytes);
    }

    /// <summary>
    /// Encrypts a seed with a password and stores it in IndexedDB.
    /// </summary>
    /// <param name="seed">The seed to store.</param>
    /// <param name="password">User password for encryption.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StoreSeedAsync(string seed, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(seed);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        try
        {
            var gunInterop = await GetGunInteropAsync();

            // Encrypt the seed with the password
            var encryptedSeed = await gunInterop.InvokeAsync<string>("encryptSeed", seed, password);

            // Store encrypted seed in IndexedDB
            await gunInterop.InvokeVoidAsync("storeSeedInIndexedDB", encryptedSeed);

            System.Diagnostics.Debug.WriteLine("[GunDbSeedService] Seed encrypted and stored in IndexedDB");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GunDbSeedService] Failed to store seed: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Deletes the encrypted seed from IndexedDB and the GunDB database.
    /// This completely wipes all local GunDB data.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task DeleteAllAsync()
    {
        try
        {
            var gunInterop = await GetGunInteropAsync();

            // Delete the seed from IndexedDB
            await gunInterop.InvokeVoidAsync("deleteSeedFromIndexedDB");

            // Delete the IndexedDB 'gun' database
            await _jsRuntime.InvokeVoidAsync("eval", @"
                new Promise((resolve, reject) => {
                    const deleteRequest = indexedDB.deleteDatabase('gun');
                    deleteRequest.onsuccess = () => resolve();
                    deleteRequest.onerror = () => reject(deleteRequest.error);
                    deleteRequest.onblocked = () => reject('Database deletion blocked');
                }).catch(err => console.error('[GunDbSeedService] Failed to delete IndexedDB:', err));
            ");

            System.Diagnostics.Debug.WriteLine("[GunDbSeedService] Successfully deleted seed and IndexedDB database");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GunDbSeedService] Failed to delete all data: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Validates a seed format (checks if it's a valid base64 string).
    /// </summary>
    /// <param name="seed">The seed to validate.</param>
    /// <returns>True if the seed is valid, false otherwise.</returns>
    public static bool IsValidSeed(string seed)
    {
        if (string.IsNullOrWhiteSpace(seed))
        {
            return false;
        }

        try
        {
            Convert.FromBase64String(seed);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets or loads the gun-interop JS module.
    /// </summary>
    private async Task<IJSObjectReference> GetGunInteropAsync()
    {
        if (_gunInterop != null)
        {
            return _gunInterop;
        }

        return await _jsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/gun-interop.js");
    }
}
