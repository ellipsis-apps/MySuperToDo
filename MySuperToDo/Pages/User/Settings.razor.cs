using System.Security.Claims;
using System.Text.Json;

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

using MySuperToDo.Domain.Entities;
using DomainUser = MySuperToDo.Domain.Entities.User;

namespace MySuperToDo.Pages.User;

public partial class Settings
{
    private UserSettings? _settings;
    private DomainUser? _user;
    private string? _settingsId;
    private string? _errorMessage;
    private bool _isSaving;
    private bool _isExporting;
    private bool _isImporting;
    private string? _exportedBlob;
    private string? _exportedDecrypted;
    private string? _exportedPublicKey;
    private bool _showImportArea;
    private string? _importBlob;
    private string? _importPassword;
    private string? _importStatus;

    protected override async Task OnInitializedAsync()
    {
        await LoadOrCreateSettingsAsync();
    }

    private async Task ExportKeypairAsync()
    {
        _isExporting = true;
        _exportedBlob = null;
        _exportedDecrypted = null;
        _exportedPublicKey = null;
        try
        {
            var module = await JS.InvokeAsync<IJSObjectReference>("import", (object)"./js/gun-interop.js");

            // First, check local storage for an existing exported encrypted seed saved at registration
            try
            {
                var stored = await LocalStorage.GetItemAsync<string>(MySuperToDo.Domain.Constants.GunDbConstants.EncryptedSeedKeyName);
                if (!string.IsNullOrWhiteSpace(stored))
                {
                    _exportedBlob = stored;
                }
            }
            catch { /* ignore local storage read errors */ }

            if (!string.IsNullOrWhiteSpace(_exportedBlob))
            {
                // Stored blob exists; show it without prompting and derive public key from current pair.
                try
                {
                    var currentPair = await module.InvokeAsync<string>("getCurrentUserPairPlain");
                    if (!string.IsNullOrWhiteSpace(currentPair))
                    {
                        try
                        {
                            using var doc2 = JsonDocument.Parse(currentPair);
                            if (doc2.RootElement.ValueKind == JsonValueKind.Object)
                            {
                                if (doc2.RootElement.TryGetProperty("pub", out var cp) && cp.ValueKind == JsonValueKind.String)
                                {
                                    _exportedPublicKey = cp.GetString();
                                }
                                else if (doc2.RootElement.TryGetProperty("epub", out var cep) && cep.ValueKind == JsonValueKind.String)
                                {
                                    _exportedPublicKey = cep.GetString();
                                }
                            }
                        }
                        catch { }
                    }
                }
                catch { }

                return;
            }

            // No stored blob found; ask for password and generate a new export.
            var pwd = await JS.InvokeAsync<string>("prompt", "Enter a password to encrypt your exported keypair:");
            if (string.IsNullOrEmpty(pwd)) return;

            _exportedBlob = await module.InvokeAsync<string>("exportEncryptedPair", pwd);

            // Also attempt to decrypt the blob locally for display (shows pair.pub etc.)
            try
            {
                var toDecrypt = _exportedBlob;
                if (string.IsNullOrWhiteSpace(toDecrypt))
                {
                    _exportedDecrypted = null;
                    return;
                }
                var decrypted = await module.InvokeAsync<string>("decryptEncryptedPair", toDecrypt, pwd);
                _exportedDecrypted = decrypted;

                // Try to extract the public key (pub or epub) for quick verification
                try
                {
                    using var doc = JsonDocument.Parse(decrypted);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        if (doc.RootElement.TryGetProperty("pair", out var pairElem) && pairElem.ValueKind == JsonValueKind.Object)
                        {
                            if (pairElem.TryGetProperty("pub", out var pubElem) && pubElem.ValueKind == JsonValueKind.String)
                            {
                                _exportedPublicKey = pubElem.GetString();
                            }
                            else if (pairElem.TryGetProperty("epub", out var epubElem) && epubElem.ValueKind == JsonValueKind.String)
                            {
                                _exportedPublicKey = epubElem.GetString();
                            }
                        }
                        else if (doc.RootElement.TryGetProperty("pub", out var rootPub) && rootPub.ValueKind == JsonValueKind.String)
                        {
                            _exportedPublicKey = rootPub.GetString();
                        }
                    }
                }
                catch { /* ignore parsing errors; leave public key null */ }
                // If decrypted payload looks empty for pair, try asking the JS side for current pair
                if (string.IsNullOrWhiteSpace(_exportedPublicKey))
                {
                    try
                    {
                        var currentPair = await module.InvokeAsync<string>("getCurrentUserPairPlain");
                        if (!string.IsNullOrWhiteSpace(currentPair))
                        {
                            try
                            {
                                using var doc2 = JsonDocument.Parse(currentPair);
                                if (doc2.RootElement.ValueKind == JsonValueKind.Object)
                                {
                                    if (doc2.RootElement.TryGetProperty("pub", out var cp) && cp.ValueKind == JsonValueKind.String)
                                    {
                                        _exportedPublicKey = cp.GetString();
                                    }
                                    else if (doc2.RootElement.TryGetProperty("epub", out var cep) && cep.ValueKind == JsonValueKind.String)
                                    {
                                        _exportedPublicKey = cep.GetString();
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
            catch (JSException dex)
            {
                // Decryption failed unexpectedly; leave decrypted area blank
                Console.Error.WriteLine($"Failed to decrypt exported blob for display: {dex.Message}");
                _exportedDecrypted = null;
            }
        }
        catch (JSException ex)
        {
            _errorMessage = "Export failed: " + ex.Message;
        }
        finally
        {
            _isExporting = false;
        }
    }

    private async Task ImportKeypairAsync()
    {
        _isImporting = true;
        _importStatus = null;
        try
        {
            if (string.IsNullOrWhiteSpace(_importBlob) || string.IsNullOrWhiteSpace(_importPassword))
            {
                _importStatus = "Both blob and password are required.";
                return;
            }

            var module = await JS.InvokeAsync<IJSObjectReference>("import", (object)"./js/gun-interop.js");
            await module.InvokeAsync<bool>("importEncryptedPair", _importBlob, _importPassword);

            // Successful import — refresh auth state by navigating home.
            Navigation.NavigateTo("/", replace: true);
        }
        catch (JSException ex)
        {
            _importStatus = "Import failed: " + ex.Message;
        }
        catch (Exception ex)
        {
            _importStatus = "Import failed: " + ex.Message;
        }
        finally
        {
            _isImporting = false;
        }
    }

    private async Task LoadOrCreateSettingsAsync()
    {
        try
        {
            _errorMessage = null;

            var authState = await AuthStateProvider.GetAuthenticationStateAsync();
            var principal = authState.User;

            if (principal.Identity?.IsAuthenticated != true)
            {
                Navigation.NavigateTo($"{Navigation.BaseUri}", replace: true);
                return;
            }

            var username = principal.FindFirst(ClaimTypes.Name)?.Value;
            var userIdFromClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var emailFromClaim = principal.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty;
            if (string.IsNullOrWhiteSpace(username))
            {
                _errorMessage = "Could not resolve the current user.";
                return;
            }

            _user = await GunDb.GetOnceAsync<DomainUser>($"users/{username}");
            if (_user is null)
            {
                _user = await ResolveUserFromUsersCollectionAsync(username, userIdFromClaim);

                if (_user is not null)
                {
                    await GunDb.PutAsync($"users/{username}", _user);
                }
                else
                {
                    _user = new DomainUser
                    {
                        Id = string.IsNullOrWhiteSpace(userIdFromClaim) ? Guid.NewGuid().ToString() : userIdFromClaim,
                        Username = username,
                        Email = emailFromClaim,
                        CreatedAt = DateTime.UtcNow
                    };

                    await GunDb.PutAsync($"users/{username}", _user);
                }
            }

            _settingsId = string.IsNullOrWhiteSpace(_user.UserSettingsId)
                ? $"{_user.Id}-settings"
                : _user.UserSettingsId;

            var needsUserLinkUpdate = string.IsNullOrWhiteSpace(_user.UserSettingsId);
            var settingsFromDb = await GunDb.GetOnceAsync<UserSettings>($"user-settings/{_settingsId}");

            if (settingsFromDb is null)
            {
                settingsFromDb = new UserSettings();
                await GunDb.PutAsync($"user-settings/{_settingsId}", settingsFromDb);
                needsUserLinkUpdate = true;
            }

            if (needsUserLinkUpdate)
            {
                _user.UserSettingsId = _settingsId;
                await GunDb.PutAsync($"users/{username}", _user);
            }

            _settings = await GunDb.GetOnceAsync<UserSettings>($"user-settings/{_settingsId}") ?? settingsFromDb;
        }
        catch (JSException ex)
        {
            _errorMessage = $"Could not load settings: {ex.Message}";
        }
        catch (InvalidOperationException ex)
        {
            _errorMessage = $"Could not load settings: {ex.Message}";
        }
    }

    private async Task<DomainUser?> ResolveUserFromUsersCollectionAsync(string username, string? userId)
    {
        DomainUser? found = null;

        var subscription = await GunDb.SubscribeMapAsync("users", (json, _) =>
        {
            if (found is not null)
            {
                return Task.CompletedTask;
            }

            var candidate = JsonSerializer.Deserialize<DomainUser>(json);
            if (candidate is null)
            {
                return Task.CompletedTask;
            }

            if (!string.IsNullOrWhiteSpace(userId) && string.Equals(candidate.Id, userId, StringComparison.Ordinal))
            {
                found = candidate;
                return Task.CompletedTask;
            }

            if (string.Equals(candidate.Username, username, StringComparison.OrdinalIgnoreCase))
            {
                found = candidate;
            }

            return Task.CompletedTask;
        });

        await Task.Delay(350);
        await subscription.DisposeAsync();

        return found;
    }

    private async Task SaveAsync()
    {
        if (_settings is null || string.IsNullOrWhiteSpace(_settingsId))
        {
            return;
        }

        _isSaving = true;
        _errorMessage = null;

        try
        {
            await GunDb.PutAsync($"user-settings/{_settingsId}", _settings);
            Navigation.NavigateTo($"{Navigation.BaseUri}", replace: true);
        }
        catch (JSException ex)
        {
            _errorMessage = $"Could not save settings: {ex.Message}";
        }
        catch (InvalidOperationException ex)
        {
            _errorMessage = $"Could not save settings: {ex.Message}";
        }
        finally
        {
            _isSaving = false;
        }
    }
}
