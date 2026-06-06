using System.Text.Json.Serialization;

namespace MySuperToDo.Domain.Entities;

public class UserProfile
{
    [JsonPropertyName("_key")]
    public string Id { get; set; } = System.Guid.NewGuid().ToString();
    public string FirstName { get; set; } = string.Empty;
}
