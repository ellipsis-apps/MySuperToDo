namespace MySuperToDo.Domain.Entities;

public class UserSettings
{
    public bool HideCompletedItems { get; set; } = false;
    public bool AllItemsCompletedCompletesList { get; set; } = true;
    public string RelayServerUrls { get; set; } = string.Empty;

    public List<string> GetRelayServerUrls()
    {
        return RelayServerUrls
            .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void SetRelayServerUrls(List<string> urls)
    {
        RelayServerUrls = string.Join(Environment.NewLine, (urls ?? []).Where(url => !string.IsNullOrWhiteSpace(url)));
    }
}