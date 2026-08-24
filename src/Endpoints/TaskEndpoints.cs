using Microsoft.EntityFrameworkCore;
using TaskApi.Data;
using TaskApi.Models;

namespace TaskApi.Endpoints;

public static class TaskEndpoints
{
    public static void MapTaskEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/tasks");

        group.MapGet("/", async (AppDbContext db) =>
            Results.Ok(await db.Tasks.AsNoTracking()
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync()));

        group.MapGet("/{id:guid}", async (Guid id, AppDbContext db) =>
            await db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id) is { } task
                ? Results.Ok(task)
                : Results.Problem(statusCode: StatusCodes.Status404NotFound,
                    title: "Tache introuvable", detail: $"Aucune tache avec l'identifiant {id}."));

        group.MapPost("/", async (CreateTaskRequest request, AppDbContext db) =>
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
        });

        group.MapPut("/{id:guid}", async (Guid id, UpdateTaskRequest request, AppDbContext db) =>
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
                return Results.Problem(statusCode: StatusCodes.Status404NotFound,
                    title: "Tache introuvable", detail: $"Aucune tache avec l'identifiant {id}.");
            }

            task.Title = request.Title!.Trim();
            task.Description = request.Description?.Trim();
            task.Status = request.Status ?? task.Status;

            await db.SaveChangesAsync();

            return Results.Ok(task);
        });

        group.MapDelete("/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id);
            if (task is null)
            {
                return Results.Problem(statusCode: StatusCodes.Status404NotFound,
                    title: "Tache introuvable", detail: $"Aucune tache avec l'identifiant {id}.");
            }

            db.Tasks.Remove(task);
            await db.SaveChangesAsync();

            return Results.NoContent();
        });
    }
}
