using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Radzen.Blazor;

using MySuperToDo.Application.Interfaces;
using MySuperToDo.Domain.Entities;
using MySuperToDo.Services;

namespace MySuperToDo.Layout;

public partial class MainLayout : LayoutComponentBase, IDisposable
{
    private bool sidebarExpanded = true;
    private bool _isAuthenticated;
    private bool _hasGunPeers;
    private string _gunPeersTooltip = "GunDB has no peers configured";
    private string _userDisplayName = "Guest";
    private string _authStatusLabel = "Not signed in";

    [Inject] public IGunDbService GunDb { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        AuthStateProvider.AuthenticationStateChanged += OnAuthStateChanged;
        GunDb.PeersChanged += OnPeersChanged;
        var state = await AuthStateProvider.GetAuthenticationStateAsync();
        ApplyAuthState(state);
        await LoadPeersAsync(state);
    }

    private void OnPeersChanged(IReadOnlyList<string> peerUrls)
    {
        _ = InvokeAsync(() =>
        {
            UpdatePeerState(peerUrls);
            StateHasChanged();
        });
    }

    private void UpdatePeerState(IReadOnlyList<string> peerUrls)
    {
        _hasGunPeers = peerUrls.Count > 0;
        _gunPeersTooltip = _hasGunPeers
            ? string.Join(Environment.NewLine, peerUrls)
            : "GunDB has no peers configured";
    }

    private void OnAuthStateChanged(Task<AuthenticationState> task)
    {
        _ = InvokeAsync(async () =>
        {
            var state = await task;
            ApplyAuthState(state);
            await LoadPeersAsync(state);
            StateHasChanged();
        });
    }

    private async Task LoadPeersAsync(AuthenticationState state)
    {
        if (state.User.Identity?.IsAuthenticated != true)
        {
            await GunDb.UpdatePeersAsync([]);
            UpdatePeerState([]);
            return;
        }

        var username = state.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            await GunDb.UpdatePeersAsync([]);
            UpdatePeerState([]);
            return;
        }

        try
        {
            // Use retries to handle transient failures during initial peer setup.
            // This resolves a circular dependency: peers are needed to read user settings,
            // but user settings contain the peer URLs. We retry to give the first read a chance.
            var user = await RetryGetAsync<User>($"users/{username}", maxAttempts: 3);
            if (user is null || string.IsNullOrWhiteSpace(user.UserSettingsId))
            {
                // User data not yet available, but don't clear peers.
                // This allows authenticated users to work with existing peer configuration.
                return;
            }

            var settings = await RetryGetAsync<UserSettings>(
                $"user-settings/{user.UserSettingsId}", maxAttempts: 3);
            var peerUrls = settings?.GetRelayServerUrls() ?? [];
            await GunDb.UpdatePeersAsync(peerUrls);
            UpdatePeerState(peerUrls);
        }
        catch (Exception ex)
        {
            // Log but don't crash on peer loading failures.
            // The user can still interact with existing data.
            System.Diagnostics.Debug.WriteLine($"[MainLayout] Failed to load peers: {ex.Message}");
        }
    }

    private async Task<T?> RetryGetAsync<T>(string path, int maxAttempts = 3) where T : class
    {
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var result = await GunDb.GetOnceAsync<T>(path);
                if (result is not null)
                {
                    return result;
                }
            }
            catch
            {
                // Ignore and retry
            }

            if (attempt < maxAttempts)
            {
                // Exponential backoff: 100ms, 200ms, etc.
                await Task.Delay(100 * attempt);
            }
        }

        return null;
    }

    private void ApplyAuthState(AuthenticationState state)
    {
        _isAuthenticated = state.User.Identity?.IsAuthenticated == true;
        _userDisplayName = _isAuthenticated
            ? state.User.Identity?.Name ?? "Signed in"
            : "Guest";
        _authStatusLabel = _isAuthenticated ? "Signed in" : "Not signed in";
    }

    public void Dispose()
    {
        AuthStateProvider.AuthenticationStateChanged -= OnAuthStateChanged;
        GunDb.PeersChanged -= OnPeersChanged;  
    }

    private async Task OnProfileMenuItemClick(RadzenSplitButtonItem item)
    {
        if (item?.Value == "settings")
        {
            Navigation.NavigateTo($"{Navigation.BaseUri}user/settings");
        }
        else if (item?.Value == "signin")
        {
            Navigation.NavigateTo($"{Navigation.BaseUri}");
        }
        else if (item?.Value == "logout")
        {
            await AuthStateProvider.SignOutAsync();
            Navigation.NavigateTo($"{Navigation.BaseUri}", forceLoad: true);
        }
    }
}
