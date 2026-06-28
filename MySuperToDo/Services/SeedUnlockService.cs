using Blazored.SessionStorage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using MySuperToDo.Application.Interfaces;
using MySuperToDo.Domain.Entities;

using System.Security.Claims;

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
    private readonly IConfiguration _configuration;
    private readonly ILogger<SeedUnlockService> _logger;

    public SeedUnlockService(
        GunDbSeedService seedService,
        IGunDbService gunDb,
        UserAuthService userAuth,
        CustomAuthenticationStateProvider authProvider,
        ISessionStorageService sessionStorage,
        IConfiguration configuration,
        ILogger<SeedUnlockService> logger)
    {
        _seedService = seedService;
        _gunDb = gunDb;
        _userAuth = userAuth;
        _authProvider = authProvider;
        _sessionStorage = sessionStorage;
        _configuration = configuration;
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
            await _authProvider.SignInAsync(user.Id, user.Username, user.Email ?? string.Empty, rememberMe: false);
            await PopulateConfiguredPeersAsync(cancellationToken);
        }
    }

    public async Task PopulateConfiguredPeersAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var authState = await _authProvider.GetAuthenticationStateAsync();
            var principal = authState.User;

            if (principal.Identity?.IsAuthenticated != true)
            {
                return;
            }

            var username = principal.FindFirst(ClaimTypes.Name)?.Value;
            if (string.IsNullOrWhiteSpace(username))
            {
                return;
            }

            var user = await _gunDb.GetOnceAsync<User>($"users/{username}", cancellationToken);
            if (user is null)
            {
                return;
            }

            var settingsId = string.IsNullOrWhiteSpace(user.UserSettingsId)
                ? $"{user.Id}-settings"
                : user.UserSettingsId;

            var settings = await _gunDb.GetOnceAsync<UserSettings>($"user-settings/{settingsId}", cancellationToken) ?? new UserSettings();
            var existingPeers = settings.GetRelayServerUrls();
            if (existingPeers.Count > 0)
            {
                await _gunDb.UpdatePeersAsync(existingPeers, cancellationToken);
                return;
            }

            var configuredPeers = _configuration.GetSection("GunDB:MyPeers").Get<List<string>>() ?? new List<string>();
            if (configuredPeers.Count == 0)
            {
                return;
            }

            settings.SetRelayServerUrls(configuredPeers);
            await _gunDb.PutAsync($"user-settings/{settingsId}", settings, cancellationToken);

            if (string.IsNullOrWhiteSpace(user.UserSettingsId))
            {
                user.UserSettingsId = settingsId;
                await _gunDb.PutAsync($"users/{username}", user, cancellationToken);
            }

            await _gunDb.UpdatePeersAsync(configuredPeers, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SeedUnlock: failed to populate configured relay peers into user settings.");
        }
    }
}
