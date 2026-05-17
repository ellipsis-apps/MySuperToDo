namespace MySuperToDo.Domain.Entities;

public class UserSettings
{
    public bool HideCompletedItems { get; set; } = false;
    public bool AllItemsCompletedCompletesList { get; set; } = true;
    public List<string> RelayServerUrls { get; set; } = new();

    public List<string> GetRelayServerUrls()
    {
        return RelayServerUrls ?? new List<string>();
    }

    public void SetRelayServerUrls(List<string> urls)
    {
        RelayServerUrls = urls ?? new List<string>();
    }
}