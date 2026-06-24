using Blazored.SessionStorage;
using Microsoft.Extensions.Logging;

using MySuperToDo.Application.Interfaces;

namespace MySuperToDo.Services;

/// <summary>
/// Handles unlocking the encrypted seed stored in IndexedDB and applying it to GunDB.
/// Stores the decrypted seed in session storage only when the user opts to remember the device.
/// </summary>
internal sealed class SeedUnlockService
{
    private const string SessionKey = "unlocked_seed";

    private readonly GunDbSeedService _seedService;
    private readonly IGunDbService _gunDb;
    private readonly UserAuthService _userAuth;
    private readonly CustomAuthenticationStateProvider _authProvider;
    private readonly ISessionStorageService _sessionStorage;
    private readonly ILogger<SeedUnlockService> _logger;

    public SeedUnlockService(
        GunDbSeedService seedService,
        IGunDbService gunDb,
        UserAuthService userAuth,
        CustomAuthenticationStateProvider authProvider,
        ISessionStorageService sessionStorage,
        ILogger<SeedUnlockService> logger)
    {
        _seedService = seedService;
        _gunDb = gunDb;
        _userAuth = userAuth;
        _authProvider = authProvider;
        _sessionStorage = sessionStorage;
        _logger = logger;
    }

    /// <summary>
    /// Attempts to silently unlock a seed from session storage and apply it.
    /// Returns true when an unlocked seed was applied.
    /// </summary>
    public async Task<bool> TrySilentUnlockAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var seed = await _sessionStorage.GetItemAsync<string?>(SessionKey, cancellationToken);
            if (string.IsNullOrWhiteSpace(seed)) return false;

            if (!GunDbSeedService.IsValidSeed(seed))
            {
                await _sessionStorage.RemoveItemAsync(SessionKey, cancellationToken);
                return false;
            }

            await ApplySeedAsync(seed, cancellationToken);
            _logger.LogInformation("SeedUnlock: silent unlock applied from session storage.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SeedUnlock: silent unlock failed.");
            return false;
        }
    }

    /// <summary>
    /// Attempts to decrypt the stored encrypted seed using the provided password and apply it.
    /// When rememberDevice is true the decrypted seed is stored in session storage for future silent unlocks.
    /// </summary>
    public async Task<bool> UnlockWithPasswordAsync(string password, bool rememberDevice, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        try
        {
            var seed = await _seedService.GetStoredSeedAsync(password);
            if (string.IsNullOrWhiteSpace(seed)) return false;

            if (rememberDevice)
            {
                await _sessionStorage.SetItemAsync(SessionKey, seed, cancellationToken);
            }

            await ApplySeedAsync(seed, cancellationToken);
            _logger.LogInformation("SeedUnlock: unlocked with password and applied.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SeedUnlock: unlock with password failed.");
            throw;
        }
    }

    /// <summary>
    /// Clears any unlocked seed from session storage and (optionally) clears runtime state.
    /// </summary>
    public async Task ClearUnlockAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _sessionStorage.RemoveItemAsync(SessionKey, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SeedUnlock: failed to clear session unlock state.");
        }
    }

    private async Task ApplySeedAsync(string seed, CancellationToken cancellationToken)
    {
        // Configure the GunDB service with the seed and trigger initialization
        _gunDb.SetSeed(seed);
        await _gunDb.GetOnceAsync<object>("_init", cancellationToken);

        // Ensure app-level user exists and sign in
        var (user, error) = await _userAuth.SignInOrRegisterAsync("seed_user", seed, cancellationToken);
        if (error is null && user is not null)
        {
            await _authProvider.SignInAsync(user.Id, user.Username, user.Email ?? string.Empty, rememberMe: true);
        }
    }
}
