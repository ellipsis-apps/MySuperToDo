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
            // Normalize URLs: strip protocol and use as key for deduplication
            .DistinctBy(url => NormalizeUrlForComparison(url))
            .ToList();
    }

    public void SetRelayServerUrls(List<string> urls)
    {
        // Validate all URLs use the same scheme
        var schemes = urls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => ExtractScheme(url))
            .Distinct()
            .ToList();

        if (schemes.Count > 1)
        {
            throw new ArgumentException(
                $"All relay URLs must use the same protocol. Found: {string.Join(", ", schemes)}. " +
                "Use either wss:// or https:// consistently.",
                nameof(urls));
        }

        RelayServerUrls = string.Join(Environment.NewLine, 
            urls?.Where(url => !string.IsNullOrWhiteSpace(url)) ?? []);
    }

    private static string ExtractScheme(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        var schemeEnd = url.IndexOf("://");
        return schemeEnd > 0 ? url[..schemeEnd].ToLowerInvariant() : "unknown";
    }

    private static string NormalizeUrlForComparison(string url)
    {
        // Remove protocol for comparison to catch duplicates with different schemes
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        var schemeEnd = url.IndexOf("://");
        var hostPath = schemeEnd > 0 ? url[(schemeEnd + 3)..] : url;

        // Normalize to lowercase for case-insensitive comparison
        return hostPath.ToLowerInvariant();
    }
}
