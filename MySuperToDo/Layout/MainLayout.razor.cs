using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Radzen.Blazor;

using MySuperToDo.Services;

namespace MySuperToDo.Layout;

public partial class MainLayout : LayoutComponentBase, IDisposable
{
    private bool sidebarExpanded = true;
    private bool _isAuthenticated;

    protected override async Task OnInitializedAsync()
    {
        AuthStateProvider.AuthenticationStateChanged += OnAuthStateChanged;
        var state = await AuthStateProvider.GetAuthenticationStateAsync();
        _isAuthenticated = state.User.Identity?.IsAuthenticated == true;
    }

    private void OnAuthStateChanged(Task<AuthenticationState> task)
    {
        _ = InvokeAsync(async () =>
        {
            var state = await task;
            _isAuthenticated = state.User.Identity?.IsAuthenticated == true;
            StateHasChanged();
        });
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
        else if (item?.Value == "logout")
        {
            await AuthStateProvider.SignOutAsync();
            Navigation.NavigateTo("/", forceLoad: true);
        }
    }
}
