namespace MySuperToDo.Domain.Entities;

public class UserSettings
{
    public bool HideCompletedItems { get; set; } = false;
    public bool AllItemsCompletedCompletesList { get; set; } = true;
}
