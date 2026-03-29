using MySuperToDo.Application.Interfaces;
using MySuperToDo.Domain.Entities;
using MySuperToDo.Domain.Enums;

namespace MySuperToDo.Services;

/// <summary>
/// Handles sign-in and self-registration against the GunDB user store.
/// Users are keyed by username at the path "users/{username}" under the reticle.
/// </summary>
internal sealed class UserAuthService(IGunDbService gun, IPasswordHasher passwordHasher)
{
    private const string DefaultListName = "All To Do Items";
    private const string AllItemsListKey  = "all-items";

    private static string UserPath(string username)  => $"users/{username}";
    private static string ListPath(string listId)    => $"lists/{listId}";
    private static string AllItemsListPath           => ListPath(AllItemsListKey);

    /// <summary>
    /// Looks up <paramref name="username"/> in GunDB.
    /// <list type="bullet">
    ///   <item>Found + password matches → ensures default list exists, returns the user.</item>
    ///   <item>Found + password wrong → returns an error.</item>
    ///   <item>Not found → creates user + default list, returns the user.</item>
    /// </list>
    /// Returns <c>(User, null)</c> on success or <c>(null, errorMessage)</c> on failure.
    /// </summary>
    public async Task<(User? User, string? Error)> SignInOrRegisterAsync(
        string username, string password, CancellationToken cancellationToken = default)
    {
        var existing = await gun.GetOnceAsync<User>(UserPath(username), cancellationToken);

        if (existing is not null)
        {
            if (!passwordHasher.VerifyPassword(password, existing.PasswordHash))
                return (null, "Invalid username or password.");

            existing.LastLoginAt = DateTime.UtcNow;
            existing = await EnsureDefaultListAsync(existing, cancellationToken);
            await gun.PutAsync(UserPath(username), existing, cancellationToken);
            return (existing, null);
        }

        var newUser = new User
        {
            Id = Guid.NewGuid().ToString(),
            Username = username,
            PasswordHash = passwordHasher.HashPassword(password),
            CreatedAt = DateTime.UtcNow,
            LastLoginAt = DateTime.UtcNow
        };

        newUser = await EnsureDefaultListAsync(newUser, cancellationToken);
        await gun.PutAsync(UserPath(username), newUser, cancellationToken);
        return (newUser, null);
    }

    /// <summary>
    /// Ensures a list named <see cref="DefaultListName"/> exists for <paramref name="user"/>.
    /// Creates it if the user has no reference to one or if the referenced node is gone.
    /// Returns the (possibly updated) user.
    /// </summary>
    private async Task<User> EnsureDefaultListAsync(User user, CancellationToken cancellationToken)
    {
        // Always check the fixed path — prevents duplicates regardless of user.AllItemsListId state.
        var existing = await gun.GetOnceAsync<ToDoList>(AllItemsListPath, cancellationToken);

        if (existing is not null && !string.IsNullOrEmpty(existing.Name))
        {
            user.AllItemsListId = AllItemsListKey;
            return user;
        }

        var list = new ToDoList
        {
            Id = AllItemsListKey,
            Name = DefaultListName,
            Status = ToDoStatus.New,
            StatusDate = DateTime.UtcNow
        };

        await gun.PutAsync(AllItemsListPath, list, cancellationToken);
        user.AllItemsListId = AllItemsListKey;
        return user;
    }
}
