namespace TaskApi.Models;

// Nomme TaskItem et non Task : System.Threading.Tasks.Task est importe par ImplicitUsings,
// et une entite nommee Task casserait toute signature async du projet.
public class TaskItem
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public TaskItemStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
