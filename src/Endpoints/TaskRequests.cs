using TaskApi.Models;

namespace TaskApi.Endpoints;

public record CreateTaskRequest(string? Title, string? Description);

public record UpdateTaskRequest(string? Title, string? Description, TaskItemStatus? Status);

public static class TaskRequestValidator
{
    private const int TitleMaxLength = 200;
    private const int DescriptionMaxLength = 2000;

    public static Dictionary<string, string[]> Validate(
        string? title, string? description, TaskItemStatus? status, bool titleRequired)
    {
        var errors = new Dictionary<string, string[]>();

        if (titleRequired && string.IsNullOrWhiteSpace(title))
        {
            errors[nameof(title)] = ["Le titre est obligatoire."];
        }
        else if (title is not null && title.Length > TitleMaxLength)
        {
            errors[nameof(title)] = [$"Le titre depasse {TitleMaxLength} caracteres."];
        }

        if (description is not null && description.Length > DescriptionMaxLength)
        {
            errors[nameof(description)] = [$"La description depasse {DescriptionMaxLength} caracteres."];
        }

        // Un enum non defini passe la deserialisation JSON sans erreur : sans ce test,
        // Status = 99 serait persiste tel quel.
        if (status is not null && !Enum.IsDefined(status.Value))
        {
            errors[nameof(status)] = [$"Statut inconnu. Valeurs admises : {string.Join(", ", Enum.GetNames<TaskItemStatus>())}."];
        }

        return errors;
    }
}
