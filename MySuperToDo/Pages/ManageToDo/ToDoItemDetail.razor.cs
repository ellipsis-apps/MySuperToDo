using MySuperToDo.Domain.Entities;
using MySuperToDo.Domain.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace MySuperToDo.Pages.ManageToDo;

public partial class ToDoItemDetail
{
    private const string AllItemsListIdFallback = "all-items";
    private const string AllItemsListName = "All To Do Items";

    private ToDoItem _item = new() { IsUrgent = false, Priority = Priority.Medium };
    private readonly Priority[] _priorities = Enum.GetValues<Priority>();
    private readonly ToDoStatus[] _statuses = Enum.GetValues<ToDoStatus>();
    private bool _isBusy;
    private string? _errorMessage;

    [Parameter] public string ListId { get; set; } = string.Empty;
    [Parameter] public string AllItemsListId { get; set; } = string.Empty;
    [Parameter] public ToDoItem? ExistingItem { get; set; }

    protected override void OnParametersSet()
    {
        if (ExistingItem is not null)
        {
            _item = new ToDoItem
            {
                Id = ExistingItem.Id,
                Title = ExistingItem.Title,
                Priority = ExistingItem.Priority,
                IsUrgent = ExistingItem.IsUrgent,
                DueDate = ExistingItem.DueDate,
                Status = ExistingItem.Status
            };
        }
    }

    private async Task OnSubmitAsync()
    {
        _isBusy = true;
        _errorMessage = null;

        try
        {
            var allItemsListId = string.IsNullOrWhiteSpace(AllItemsListId)
                ? AllItemsListIdFallback
                : AllItemsListId;

            await EnsureAllItemsListExistsAsync(allItemsListId);

            await GunDb.PutAsync($"items/{_item.Id}", _item);

            if (!string.IsNullOrWhiteSpace(ListId))
            {
                await GunDb.PutAsync($"list-items/{ListId}/{_item.Id}", new { ItemId = _item.Id });
            }

            if (!string.IsNullOrWhiteSpace(allItemsListId))
            {
                await GunDb.PutAsync($"list-items/{allItemsListId}/{_item.Id}", new { ItemId = _item.Id });
            }

            DialogService.Close(_item);
        }
        catch (InvalidOperationException ex)
        {
            _errorMessage = $"Could not save item: {ex.Message}";
        }
        catch (JSException ex)
        {
            _errorMessage = $"Could not save item: {ex.Message}";
        }
        finally
        {
            _isBusy = false;
        }
    }

    private async Task EnsureAllItemsListExistsAsync(string allItemsListId)
    {
        var existing = await GunDb.GetOnceAsync<ToDoList>($"lists/{allItemsListId}");
        if (existing is not null && !string.IsNullOrWhiteSpace(existing.Name))
        {
            return;
        }

        await GunDb.PutAsync($"lists/{allItemsListId}", new ToDoList
        {
            Id = allItemsListId,
            Name = AllItemsListName,
            Status = ToDoStatus.New,
            StatusDate = DateTime.UtcNow
        });
    }

    private void OnCancel() => DialogService.Close(null);
}
