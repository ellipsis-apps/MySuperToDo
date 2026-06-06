using Microsoft.AspNetCore.Components;

namespace MySuperToDo.Pages;

public partial class Home
{
    private readonly LoginModel _model = new();
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        var authState = await AuthStateProvider.GetAuthenticationStateAsync();
        if (authState.User.Identity?.IsAuthenticated == true)
        {
            Navigation.NavigateTo("/lists", replace: true);
        }
    }

    private async Task OnSubmitAsync()
    {
        _isBusy = true;
        _errorMessage = string.Empty;
        StateHasChanged();

        var (user, error) = await UserAuth.SignInOrRegisterAsync(_model.UserName, _model.Password);

        if (error is not null)
        {
            _errorMessage = error;
            _isBusy = false;
            return;
        }

        await AuthStateProvider.SignInAsync(user!.Id, user.Username, user.Email, rememberMe: true);
        Navigation.NavigateTo("/lists", replace: true);
    }

    private sealed class LoginModel
    {
        public string UserName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}
