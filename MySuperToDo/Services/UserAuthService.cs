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
    private static bool IsCorrupted(User user) =>
        string.IsNullOrWhiteSpace(user.Username) || string.IsNullOrWhiteSpace(user.PasswordHash);

    /// <summary>
    /// Looks up <paramref name="username"/> in GunDB.
    /// <list type="bullet">
    ///   <item>Found + password matches → ensures default list exists, returns the user.</item>
    ///   <item>Found + password wrong → returns an error.</item>
    ///   <item>Not found → creates user + default list, returns the user.</item>
    /// </list>
    /// Returns <c>(User, null)</c> on success or <c>(null, errorMessage)</c> on failure.
    /// The boolean indicates whether the user was newly created.
    /// </summary>
    public async Task<(User? User, string? Error, bool Created)> SignInOrRegisterAsync(
        string username, string password, CancellationToken cancellationToken = default)
    {
        // Normalize username to avoid duplicate accounts caused by casing or surrounding whitespace
        var normalizedUsername = (username ?? string.Empty).Trim().ToLowerInvariant();

        // Best-effort: first perform Gun/SEA login or registration so the user's data
        // (which may be encrypted) can replicate to this client before we read it.
        try
        {
            await gun.LoginOrRegisterAsync(normalizedUsername, password, cancellationToken);
        }
        catch
        {
            // ignore interop errors here; we will still attempt to read the user record below
        }

        // Attempt to read the canonical user record. If replication is slightly delayed,
        // retry a few times before creating a new user to avoid duplicate records.
        User? existing = null;
        const int maxAttempts = 4;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            existing = await gun.GetOnceAsync<User>(UserPath(normalizedUsername), cancellationToken);
            if (existing is not null)
                break;

            if (attempt < maxAttempts)
            {
                // small backoff to allow peers to replicate
                try { await Task.Delay(250 * attempt, cancellationToken); } catch { break; }
            }
        }

        if (existing is not null && IsCorrupted(existing))
        {
            await CleanupCorruptedUserAsync(normalizedUsername, cancellationToken);
            existing = null;
        }

        if (existing is not null)
        {
            if (!passwordHasher.VerifyPassword(password, existing.PasswordHash))
                return (null, "Invalid username or password.", false);

            existing.LastLoginAt = DateTime.UtcNow;
            existing = await EnsureDefaultListAsync(existing, cancellationToken);
            // persist using the normalized username key to prevent duplicates
            await gun.PutAsync(UserPath(normalizedUsername), existing, cancellationToken);
            return (existing, null, false);
        }

        // No canonical user record found after retries — create one deterministically
        var newUser = new User
        {
            Id = Guid.NewGuid().ToString(),
            // store normalized username as the canonical key/username
            Username = normalizedUsername,
            PasswordHash = passwordHasher.HashPassword(password),
            CreatedAt = DateTime.UtcNow,
            LastLoginAt = DateTime.UtcNow
        };

        newUser = await EnsureDefaultListAsync(newUser, cancellationToken);
        await gun.PutAsync(UserPath(normalizedUsername), newUser, cancellationToken);

        return (newUser, null, true);
    }

    private async Task CleanupCorruptedUserAsync(string username, CancellationToken cancellationToken)
    {
        await gun.RemoveAsync(UserPath(username), cancellationToken);
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
