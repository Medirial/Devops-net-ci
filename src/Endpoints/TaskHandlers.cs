using Microsoft.EntityFrameworkCore;
using TaskApi.Data;
using TaskApi.Models;

namespace TaskApi.Endpoints;

// Handlers sortis du mapping : tant qu'ils vivaient en lambdas dans MapTaskEndpoints,
// les atteindre imposait de demarrer un serveur. Ici ils s'appellent comme des methodes,
// avec un DbContext en parametre -- testables sans hote HTTP.
public static class TaskHandlers
{
    public static async Task<IResult> List(AppDbContext db) =>
        Results.Ok(await db.Tasks.AsNoTracking()
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync());

    public static async Task<IResult> GetById(Guid id, AppDbContext db) =>
        await db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id) is { } task
            ? Results.Ok(task)
            : NotFound(id);

    public static async Task<IResult> Create(CreateTaskRequest request, AppDbContext db)
    {
        var errors = TaskRequestValidator.Validate(
            request.Title, request.Description, status: null, titleRequired: true);

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            Title = request.Title!.Trim(),
            Description = request.Description?.Trim(),
            Status = TaskItemStatus.Todo,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        return Results.Created($"/tasks/{task.Id}", task);
    }

    public static async Task<IResult> Update(Guid id, UpdateTaskRequest request, AppDbContext db)
    {
        var errors = TaskRequestValidator.Validate(
            request.Title, request.Description, request.Status, titleRequired: true);

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id);
        if (task is null)
        {
            return NotFound(id);
        }

        task.Title = request.Title!.Trim();
        task.Description = request.Description?.Trim();

        // Statut absent du corps = statut inchange. Un PUT qui remet Todo par defaut
        // ferait regresser silencieusement une tache en cours.
        task.Status = request.Status ?? task.Status;

        await db.SaveChangesAsync();

        return Results.Ok(task);
    }

    public static async Task<IResult> Delete(Guid id, AppDbContext db)
    {
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id);
        if (task is null)
        {
            return NotFound(id);
        }

        db.Tasks.Remove(task);
        await db.SaveChangesAsync();

        return Results.NoContent();
    }

    private static IResult NotFound(Guid id) =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound,
            title: "Tache introuvable", detail: $"Aucune tache avec l'identifiant {id}.");
}
