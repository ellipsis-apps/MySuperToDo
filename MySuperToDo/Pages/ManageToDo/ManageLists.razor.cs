using System.Text.Json;
using Radzen;
using Radzen.Blazor;

using MySuperToDo.Domain.Entities;
using MySuperToDo.Domain.Enums;

namespace MySuperToDo.Pages.ManageToDo;

/// <summary>
/// Represents a Blazor component for managing ToDo lists.
/// This component displays a grid of ToDo lists, allows adding, editing, and deleting lists,
/// and subscribes to real-time updates from GunDB.
/// </summary>
public partial class ManageLists : IAsyncDisposable
{
    private RadzenDataGrid<ToDoList>? _grid;
    private IAsyncDisposable? _listsSubscription;
    private readonly Dictionary<string, ToDoList> _lists = new();
    private List<ToDoList> _displayLists = [];

    /// <summary>
    /// Called when the component is initialized asynchronously.
    /// Subscribes to the "lists" map in GunDB to receive real-time updates.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected override async Task OnInitializedAsync()
    {
        _listsSubscription = await GunDb.SubscribeMapAsync("lists", OnListReceivedAsync);
    }

    /// <summary>
    /// Handles the receipt of a list update from GunDB asynchronously.
    /// Deserializes the JSON data, updates the internal lists dictionary, and refreshes the display list.
    /// Logs errors if deserialization fails.
    /// </summary>
    /// <param name="json">The JSON string representing the ToDo list.</param>
    /// <param name="soul">The soul (key) of the list in GunDB.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private Task OnListReceivedAsync(string json, string soul)
    {
        try
        {
            var list = JsonSerializer.Deserialize<ToDoList>(json);
            if (list is not null && !string.IsNullOrEmpty(list.Name))
            {
                _lists[soul] = list;
                _displayLists = [.. _lists.Values];
                _ = InvokeAsync(StateHasChanged);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ManageLists] Failed to process list: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Opens a dialog to add a new ToDo list asynchronously.
    /// Launches the ToDoListDetail component in a dialog with specified options.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task AddNewListAsync()
    {
        await DialogService.OpenAsync<ToDoListDetail>(
            "To Do List Detail",
            null,
            new DialogOptions { Width = "420px", ShowClose = true, CloseDialogOnOverlayClick = false });
    }

    /// <summary>
    /// Opens a dialog to edit an existing ToDo list asynchronously.
    /// Launches the ToDoListDetail component in a dialog with the existing list data and specified options.
    /// </summary>
    /// <param name="list">The ToDo list to edit.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task EditListAsync(ToDoList list)
    {
        await DialogService.OpenAsync<ToDoListDetail>(
            "To Do List Detail",
            new Dictionary<string, object> { { "ExistingList", list } },
            new DialogOptions { Width = "420px", ShowClose = true, CloseDialogOnOverlayClick = false });
    }

    /// <summary>
    /// Deletes a ToDo list after user confirmation asynchronously.
    /// Shows a confirmation dialog; if confirmed, removes the list from GunDB and updates the internal lists.
    /// </summary>
    /// <param name="list">The ToDo list to delete.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task DeleteListAsync(ToDoList list)
    {
        var confirmed = await DialogService.Confirm(
            $"Delete list \"{list.Name}\"?",
            "Delete List",
            new ConfirmOptions { OkButtonText = "Delete", CancelButtonText = "Cancel" });

        if (confirmed == true)
        {
            await GunDb.RemoveAsync($"lists/{list.Id}");
            var soul = _lists.FirstOrDefault(kv => kv.Value.Id == list.Id).Key;
            if (soul is not null)
            {
                _lists.Remove(soul);
                _displayLists = [.. _lists.Values];
                StateHasChanged();
            }
        }
    }

    /// <summary>
    /// Gets the appropriate badge style for a given ToDo status.
    /// Maps ToDoStatus to Radzen BadgeStyle for visual representation.
    /// </summary>
    /// <param name="status">The ToDo status.</param>
    /// <returns>The corresponding BadgeStyle.</returns>
    private static BadgeStyle GetStatusBadgeStyle(ToDoStatus status) => status switch
    {
        ToDoStatus.Completed => BadgeStyle.Success,
        ToDoStatus.InProgress => BadgeStyle.Warning,
        _ => BadgeStyle.Info
    };

    /// <summary>
    /// Disposes of the component's resources asynchronously.
    /// Disposes the GunDB subscription if it exists.
    /// </summary>
    /// <returns>A ValueTask representing the asynchronous disposal.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_listsSubscription is not null)
        {
            await _listsSubscription.DisposeAsync();
        }
    }
}
