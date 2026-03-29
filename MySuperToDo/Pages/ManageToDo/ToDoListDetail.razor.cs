using MySuperToDo.Domain.Entities;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace MySuperToDo.Pages.ManageToDo;

public partial class ToDoListDetail
{
    private ToDoList _list = new() { IsUrgent = false };
    private bool _isBusy;
    private string? _errorMessage;

    [Parameter] public ToDoList? ExistingList { get; set; }

    protected override void OnParametersSet()
    {
        if (ExistingList is not null)
        {
            _list = new ToDoList
            {
                Id = ExistingList.Id,
                Name = ExistingList.Name,
                Status = ExistingList.Status,
                IsUrgent = ExistingList.IsUrgent,
                DueDate = ExistingList.DueDate
            };
        }
    }

    private async Task OnSubmitAsync()
    {
        _isBusy = true;
        _errorMessage = null;

        try
        {
            await GunDb.PutAsync($"lists/{_list.Id}", _list);
            DialogService.Close(_list);
        }
        catch (InvalidOperationException ex)
        {
            _errorMessage = $"Could not save list: {ex.Message}";
        }
        catch (JSException ex)
        {
            _errorMessage = $"Could not save list: {ex.Message}";
        }
        finally
        {
            _isBusy = false;
        }
    }

    private void OnCancel() => DialogService.Close(null);
}
