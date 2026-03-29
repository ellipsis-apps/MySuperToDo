using System.Text.Json.Serialization;

using MySuperToDo.Domain.Enums;

namespace MySuperToDo.Domain.Entities;

public class ToDoList
{
	[JsonPropertyName("_key")]
	public string Id { get; set; } = Guid.NewGuid().ToString();
	public string Name { get; set; } = string.Empty;
	public ToDoStatus Status { get; set; } = ToDoStatus.New;
	public List<ToDoList>? Lists { get; set; }
	public List<ToDoItem>? Items { get; set; }
	public bool IsUrgent { get; set; } = false;
	public DateTime? DueDate { get; set; }
	public DateTime StatusDate { get; set; } = DateTime.UtcNow;

}
