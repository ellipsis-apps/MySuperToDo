using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components;

using MySuperToDo.Domain.Entities;

namespace MySuperToDo.Pages.ManageToDo;

public partial class ToDoListsDialog
{
    [Parameter] public ToDoItem? Item { get; set; }
    [Parameter] public List<ToDoList>? Lists { get; set; }
    [Parameter] public string AllItemsListId { get; set; } = string.Empty;

    private readonly List<ListMembershipEntry> _memberships = [];
    private bool _isBusy;
    private string? _errorMessage;

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

    private void OnClose() => DialogService.Close();

    private sealed class ListMembershipEntry(string listId, string listName, bool isChecked)
    {
        public string ListId { get; } = listId;
        public string ListName { get; } = listName;
        public bool IsChecked { get; set; } = isChecked;
    }

    private sealed class ListItemLink
    {
        public string ItemId { get; set; } = string.Empty;
    }
}
