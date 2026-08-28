using TaskApi.Models;

namespace TaskApi.Endpoints;

public static class TaskEndpoints
{
    public static void MapTaskEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/tasks").WithTags("Tasks");

        group.MapGet("/", TaskHandlers.List)
            .WithName("ListTasks")
            .Produces<List<TaskItem>>();

        group.MapGet("/{id:guid}", TaskHandlers.GetById)
            .WithName("GetTask")
            .Produces<TaskItem>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", TaskHandlers.Create)
            .WithName("CreateTask")
            .Produces<TaskItem>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        group.MapPut("/{id:guid}", TaskHandlers.Update)
            .WithName("UpdateTask")
            .Produces<TaskItem>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", TaskHandlers.Delete)
            .WithName("DeleteTask")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
