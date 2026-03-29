using System.Text.Json.Serialization;
using MySuperToDo.Domain.Enums;

namespace MySuperToDo.Domain.Entities;

public class ToDoItem
{
	[JsonPropertyName("_key")]
	public string Id { get; set; } = Guid.NewGuid().ToString();
	public ToDoStatus Status { get; set; } = ToDoStatus.New;
	public string Title { get; set; } = string.Empty;
	[JsonConverter(typeof(JsonStringEnumConverter))]
	public Priority Priority { get; set; } = Priority.Medium;
	public bool IsUrgent { get; set; } = false;
	public DateTime? DueDate { get; set; }
}
