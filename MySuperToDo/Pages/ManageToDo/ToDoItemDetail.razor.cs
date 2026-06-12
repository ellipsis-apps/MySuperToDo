using MySuperToDo.Domain.Entities;
using MySuperToDo.Domain.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace MySuperToDo.Pages.ManageToDo;

/// <summary>
/// Represents a Blazor component for managing the details of a ToDo item.
/// This component allows creating or editing a ToDo item, including setting properties like title, priority, urgency, due date, and status.
/// It integrates with GunDB for data persistence, ensures the "All To Do Items" list exists, and uses a dialog service for user interaction.
/// </summary>
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

    /// <summary>
    /// Called when the component's parameters are set.
    /// If an existing ToDo item is provided via the ExistingItem parameter, it initializes the local _item with a copy of its properties.
    /// </summary>
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

    /// <summary>
    /// Handles the submission of the ToDo item form asynchronously.
    /// Ensures the "All To Do Items" list exists, saves the item to GunDB, and adds it to the specified list and the "All To Do Items" list if applicable.
    /// Closes the dialog with the saved item on success, or sets an error message on failure.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
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

    /// <summary>
    /// Ensures that the "All To Do Items" list exists in GunDB asynchronously.
    /// Checks if the list already exists; if not, creates it with default properties.
    /// </summary>
    /// <param name="allItemsListId">The ID of the "All To Do Items" list.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
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

    /// <summary>
    /// Handles the cancellation of the ToDo item form.
    /// Closes the dialog without saving any changes, passing null to indicate cancellation.
    /// </summary>
    private void OnCancel() => DialogService.Close(null);
}
