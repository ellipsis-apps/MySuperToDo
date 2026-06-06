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

        var user = await GunDb.GetOnceAsync<User>($"users/{username}");
        if (user is null || string.IsNullOrWhiteSpace(user.UserSettingsId))
        {
            await GunDb.UpdatePeersAsync([]);
            UpdatePeerState([]);
            return;
        }

        var settings = await GunDb.GetOnceAsync<UserSettings>($"user-settings/{user.UserSettingsId}");
        var peerUrls = settings?.GetRelayServerUrls() ?? [];
        await GunDb.UpdatePeersAsync(peerUrls);
        UpdatePeerState(peerUrls);
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
            Navigation.NavigateTo("/user/settings");
        }
        else if (item?.Value == "signin")
        {
            Navigation.NavigateTo("/");
        }
        else if (item?.Value == "logout")
        {
            await AuthStateProvider.SignOutAsync();
            Navigation.NavigateTo("/", forceLoad: true);
        }
    }
}
