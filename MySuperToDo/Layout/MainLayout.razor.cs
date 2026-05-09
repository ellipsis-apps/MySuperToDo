using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Radzen.Blazor;

using MySuperToDo.Application.Interfaces;
using MySuperToDo.Services;

namespace MySuperToDo.Layout;

public partial class MainLayout : LayoutComponentBase, IDisposable
{
    private bool sidebarExpanded = true;
    private bool _isAuthenticated;
    private bool _hasGunPeers;
    private string _userDisplayName = "Guest";
    private string _authStatusLabel = "Not signed in";

    [Inject] public IGunDbService GunDb { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        AuthStateProvider.AuthenticationStateChanged += OnAuthStateChanged;
        var state = await AuthStateProvider.GetAuthenticationStateAsync();
        ApplyAuthState(state);
        _hasGunPeers = GunDb.HasPeers;
    }

    private void OnAuthStateChanged(Task<AuthenticationState> task)
    {
        _ = InvokeAsync(async () =>
        {
            var state = await task;
            ApplyAuthState(state);
            StateHasChanged();
        });
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
