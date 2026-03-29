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

    protected override async Task OnInitializedAsync()
    {
        await LoadOrCreateSettingsAsync();
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
                Navigation.NavigateTo("/", replace: true);
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
            Navigation.NavigateTo("/", replace: true);
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
