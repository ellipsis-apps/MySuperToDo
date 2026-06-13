using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

using MySuperToDo.Domain.Entities;

using DomainUser = MySuperToDo.Domain.Entities.User;

namespace MySuperToDo.Pages.User;

public partial class RelaySetupDialog
{
    [Parameter] public string Username { get; set; } = string.Empty;
    [Parameter] public string? UserId { get; set; }

    private string _relayServersText = string.Empty;
    private string? _errorMessage;
    private bool _isSaving;

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var user = await GunDb.GetOnceAsync<DomainUser>($"users/{Username}");
            if (user is null && !string.IsNullOrWhiteSpace(UserId))
            {
                user = await ResolveUserFromUsersCollectionAsync();
            }

            if (user is null)
            {
                _errorMessage = "Could not load the current user settings.";
                return;
            }

            var settingsId = string.IsNullOrWhiteSpace(user.UserSettingsId)
                ? $"{user.Id}-settings"
                : user.UserSettingsId;

            var settings = await GunDb.GetOnceAsync<UserSettings>($"user-settings/{settingsId}") ?? new UserSettings();
            _relayServersText = string.Join(Environment.NewLine, settings.GetRelayServerUrls());
        }
        catch (JSException ex)
        {
            _errorMessage = ex.Message;
        }
    }

    private async Task<DomainUser?> ResolveUserFromUsersCollectionAsync()
    {
        DomainUser? found = null;

        var subscription = await GunDb.SubscribeMapAsync("users", (json, _) =>
        {
            if (found is not null)
            {
                return Task.CompletedTask;
            }

            var candidate = System.Text.Json.JsonSerializer.Deserialize<DomainUser>(json);
            if (candidate is null)
            {
                return Task.CompletedTask;
            }

            if (string.Equals(candidate.Id, UserId, StringComparison.Ordinal) ||
                string.Equals(candidate.Username, Username, StringComparison.OrdinalIgnoreCase))
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
        _isSaving = true;
        _errorMessage = null;

        try
        {
            var relayServers = _relayServersText
                .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(server => !string.IsNullOrWhiteSpace(server))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var user = await GunDb.GetOnceAsync<DomainUser>($"users/{Username}");
            if (user is null && !string.IsNullOrWhiteSpace(UserId))
            {
                user = await ResolveUserFromUsersCollectionAsync();
            }

            if (user is null)
            {
                _errorMessage = "Could not load the current user settings.";
                return;
            }

            var settingsId = string.IsNullOrWhiteSpace(user.UserSettingsId)
                ? $"{user.Id}-settings"
                : user.UserSettingsId;

            var settings = await GunDb.GetOnceAsync<UserSettings>($"user-settings/{settingsId}") ?? new UserSettings();
            settings.SetRelayServerUrls(relayServers);
            await GunDb.PutAsync($"user-settings/{settingsId}", settings);
            DialogService.Close(true);
        }
        catch (ArgumentException ex) when (ex.Message.Contains("same protocol"))
        {
            _errorMessage = ex.Message;
        }
        catch (JSException ex)
        {
            _errorMessage = $"Could not save relay servers: {ex.Message}";
        }
        finally
        {
            _isSaving = false;
        }
    }

    private void Cancel() => DialogService.Close(false);
}