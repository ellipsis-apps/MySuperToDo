using Blazored.LocalStorage;
using Blazored.SessionStorage;

using Microsoft.AspNetCore.Components.Authorization;

using System.Security.Claims;

namespace MySuperToDo.Services;

/// <summary>
/// Manages Blazor authentication state backed by browser localStorage (remember me)
/// or sessionStorage (session only) via Blazored.LocalStorage.
/// </summary>
public class CustomAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly ILocalStorageService _localStorage;
    private readonly ISessionStorageService _sessionStorage;
    private const string StorageKey = "auth_user";
    private ClaimsPrincipal _currentUser = new(new ClaimsIdentity());

    public CustomAuthenticationStateProvider(
        ILocalStorageService localStorage,
        ISessionStorageService sessionStorage)
    {
        _localStorage = localStorage;
        _sessionStorage = sessionStorage;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            var userInfo = await _localStorage.GetItemAsync<AuthUserInfo>(StorageKey)
                        ?? await _sessionStorage.GetItemAsync<AuthUserInfo>(StorageKey);

            _currentUser = userInfo is null
                ? new ClaimsPrincipal(new ClaimsIdentity())
                : CreatePrincipal(userInfo);

            return new AuthenticationState(_currentUser);
        }
        catch
        {
            _currentUser = new ClaimsPrincipal(new ClaimsIdentity());
            return new AuthenticationState(_currentUser);
        }
    }

    /// <summary>Signs the user in and persists their identity in browser storage.</summary>
    public async Task SignInAsync(string userId, string username, string email, bool rememberMe = false)
    {
        var userInfo = new AuthUserInfo(userId, username, email);

        if (rememberMe)
            await _localStorage.SetItemAsync(StorageKey, userInfo);
        else
            await _sessionStorage.SetItemAsync(StorageKey, userInfo);

        _currentUser = CreatePrincipal(userInfo);
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_currentUser)));
    }

    /// <summary>Signs the user out and removes their identity from browser storage.</summary>
    public async Task SignOutAsync()
    {
        await _localStorage.RemoveItemAsync(StorageKey);
        await _sessionStorage.RemoveItemAsync(StorageKey);
        _currentUser = new ClaimsPrincipal(new ClaimsIdentity());
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_currentUser)));
    }

    /// <summary>Returns the current authenticated user's ID, or null if not authenticated.</summary>
    public string? GetUserId() =>
        _currentUser.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    private static ClaimsPrincipal CreatePrincipal(AuthUserInfo userInfo)
    {
        Claim[] claims =
        [
            new(ClaimTypes.NameIdentifier, userInfo.UserId),
            new(ClaimTypes.Name, userInfo.Username),
            new(ClaimTypes.Email, userInfo.Email),
        ];
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "custom"));
    }

    private record AuthUserInfo(string UserId, string Username, string Email);
}

