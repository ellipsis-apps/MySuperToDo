using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components;

using MySuperToDo.Domain.Entities;

namespace MySuperToDo.Pages.ManageToDo;

/// <summary>
/// Represents a Blazor dialog component for managing the membership of a ToDo item in various ToDo lists.
/// This component displays a list of available ToDo lists and allows the user to add or remove the item from them.
/// It integrates with GunDB for data persistence and uses a dialog service for user interaction.
/// </summary>
public partial class ToDoListsDialog
{
    [Parameter] public ToDoItem? Item { get; set; }
    [Parameter] public List<ToDoList>? Lists { get; set; }
    [Parameter] public string AllItemsListId { get; set; } = string.Empty;

    private readonly List<ListMembershipEntry> _memberships = [];
    private bool _isBusy;
    private string? _errorMessage;

    /// <summary>
    /// Called when the component's parameters are set asynchronously.
    /// Initializes the list of memberships by filtering candidate lists (excluding "All To Do Items" and the AllItemsListId if provided),
    /// checking existing memberships in GunDB, and populating the _memberships list.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected override async Task OnParametersSetAsync()
    {
        ArgumentNullException.ThrowIfNull(Item);

        _memberships.Clear();

        var candidateLists = (Lists ?? [])
            .Where(l => !string.Equals(l.Name, "All To Do Items", StringComparison.OrdinalIgnoreCase))
            .Where(l => string.IsNullOrEmpty(AllItemsListId) || l.Id != AllItemsListId)
            .OrderBy(l => l.Name)
            .ToList();

        foreach (var list in candidateLists)
        {
            var existing = await GunDb.GetOnceAsync<ListItemLink>($"list-items/{list.Id}/{Item.Id}");
            var isChecked = existing is not null && !string.IsNullOrWhiteSpace(existing.ItemId);

            _memberships.Add(new ListMembershipEntry(list.Id, list.Name, isChecked));
        }
    }

    /// <summary>
    /// Handles changes to the membership of the ToDo item in a specific list asynchronously.
    /// Adds or removes the item from the list in GunDB based on the isChecked parameter and updates the entry's state.
    /// Sets an error message if the operation fails due to JSException or InvalidOperationException.
    /// </summary>
    /// <param name="entry">The list membership entry being changed.</param>
    /// <param name="isChecked">True to add the item to the list, false to remove it.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task OnMembershipChangedAsync(ListMembershipEntry entry, bool isChecked)
    {
        ArgumentNullException.ThrowIfNull(Item);

        _errorMessage = null;
        _isBusy = true;

        try
        {
            if (isChecked)
            {
                await GunDb.PutAsync($"list-items/{entry.ListId}/{Item.Id}", new ListItemLink { ItemId = Item.Id });
            }
            else
            {
                await GunDb.RemoveAsync($"list-items/{entry.ListId}/{Item.Id}");
            }

            entry.IsChecked = isChecked;
        }
        catch (JSException ex)
        {
            _errorMessage = $"Could not update list membership: {ex.Message}";
        }
        catch (InvalidOperationException ex)
        {
            _errorMessage = $"Could not update list membership: {ex.Message}";
        }
        finally
        {
            _isBusy = false;
        }
    }

    /// <summary>
    /// Handles the closing of the dialog.
    /// Closes the dialog without any additional actions.
    /// </summary>
    private void OnClose() => DialogService.Close();

    /// <summary>
    /// Represents an entry for a ToDo list's membership status for the current item.
    /// Contains the list ID, name, and whether the item is currently checked (i.e., a member).
    /// </summary>
    /// <param name="listId">The unique identifier of the ToDo list.</param>
    /// <param name="listName">The name of the ToDo list.</param>
    /// <param name="isChecked">True if the item is a member of this list, false otherwise.</param>
    private sealed class ListMembershipEntry(string listId, string listName, bool isChecked)
    {
        public string ListId { get; } = listId;
        public string ListName { get; } = listName;
        public bool IsChecked { get; set; } = isChecked;
    }

    /// <summary>
    /// Represents a link between a ToDo list and a ToDo item in GunDB.
    /// Contains the item ID to establish the relationship.
    /// </summary>
    private sealed class ListItemLink
    {
        public string ItemId { get; set; } = string.Empty;
    }
}
