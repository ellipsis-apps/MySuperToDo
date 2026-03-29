using System.Text.Json;
using Radzen;
using Radzen.Blazor;

using MySuperToDo.Domain.Entities;
using MySuperToDo.Domain.Enums;

namespace MySuperToDo.Pages.ManageToDo;

public partial class ManageLists : IAsyncDisposable
{
    private RadzenDataGrid<ToDoList>? _grid;
    private IAsyncDisposable? _listsSubscription;
    private readonly Dictionary<string, ToDoList> _lists = new();
    private List<ToDoList> _displayLists = [];

    protected override async Task OnInitializedAsync()
    {
        _listsSubscription = await GunDb.SubscribeMapAsync("lists", OnListReceivedAsync);
    }

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

    private async Task AddNewListAsync()
    {
        await DialogService.OpenAsync<ToDoListDetail>(
            "To Do List Detail",
            null,
            new DialogOptions { Width = "420px", ShowClose = true, CloseDialogOnOverlayClick = false });
    }

    private async Task EditListAsync(ToDoList list)
    {
        await DialogService.OpenAsync<ToDoListDetail>(
            "To Do List Detail",
            new Dictionary<string, object> { { "ExistingList", list } },
            new DialogOptions { Width = "420px", ShowClose = true, CloseDialogOnOverlayClick = false });
    }

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

    private static BadgeStyle GetStatusBadgeStyle(ToDoStatus status) => status switch
    {
        ToDoStatus.Completed => BadgeStyle.Success,
        ToDoStatus.InProgress => BadgeStyle.Warning,
        _ => BadgeStyle.Info
    };

    public async ValueTask DisposeAsync()
    {
        if (_listsSubscription is not null)
        {
            await _listsSubscription.DisposeAsync();
        }
    }
}
