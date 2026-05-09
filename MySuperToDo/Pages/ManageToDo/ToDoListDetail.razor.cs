using MySuperToDo.Domain.Entities;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace MySuperToDo.Pages.ManageToDo;

/// <summary>
/// Represents a Blazor component for managing the details of a ToDo list.
/// This component allows creating or editing a ToDo list, including setting properties like name, status, urgency, and due date.
/// It integrates with GunDB for data persistence and uses a dialog service for user interaction.
/// </summary>
public partial class ToDoListDetail
{
    private ToDoList _list = new() { IsUrgent = false };
    private bool _isBusy;
    private string? _errorMessage;

    [Parameter] public ToDoList? ExistingList { get; set; }

    /// <summary>
    /// Called when the component's parameters are set.
    /// If an existing ToDo list is provided via the ExistingList parameter, it initializes the local _list with a copy of its properties.
    /// </summary>
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

    /// <summary>
    /// Handles the submission of the ToDo list form asynchronously.
    /// Attempts to save the list to GunDB and closes the dialog with the saved list on success.
    /// Sets an error message if saving fails due to InvalidOperationException or JSException.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
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

    /// <summary>
    /// Handles the cancellation of the ToDo list form.
    /// Closes the dialog without saving any changes, passing null to indicate cancellation.
    /// </summary>
    private void OnCancel() => DialogService.Close(null);
}
