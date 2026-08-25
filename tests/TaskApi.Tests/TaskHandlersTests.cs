using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using TaskApi.Endpoints;
using TaskApi.Models;

namespace TaskApi.Tests;

public class TaskHandlersTests
{
    [Fact]
    public async Task List_returns_the_most_recent_task_first()
    {
        using var db = new TestDatabase();
        var now = DateTimeOffset.UtcNow;
        db.Seed("Ancienne", createdAt: now.AddHours(-2));
        db.Seed("Recente", createdAt: now);

        var result = await TaskHandlers.List(db.Context);

        var ok = Assert.IsType<Ok<List<TaskItem>>>(result);
        Assert.Collection(ok.Value!,
            first => Assert.Equal("Recente", first.Title),
            second => Assert.Equal("Ancienne", second.Title));
    }

    [Fact]
    public async Task List_returns_an_empty_collection_when_there_is_nothing()
    {
        using var db = new TestDatabase();

        var result = await TaskHandlers.List(db.Context);

        // Une liste vide, pas un 404 : la collection existe, elle est vide.
        var ok = Assert.IsType<Ok<List<TaskItem>>>(result);
        Assert.Empty(ok.Value!);
    }

    [Fact]
    public async Task GetById_returns_the_task_when_it_exists()
    {
        using var db = new TestDatabase();
        var seeded = db.Seed("Relire la PR");

        var result = await TaskHandlers.GetById(seeded.Id, db.Context);

        var ok = Assert.IsType<Ok<TaskItem>>(result);
        Assert.Equal(seeded.Id, ok.Value!.Id);
    }

    [Fact]
    public async Task GetById_returns_404_when_the_identifier_is_unknown()
    {
        using var db = new TestDatabase();

        var result = await TaskHandlers.GetById(Guid.NewGuid(), db.Context);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
    }

    [Fact]
    public async Task Create_persists_the_task_and_returns_its_location()
    {
        using var db = new TestDatabase();

        var result = await TaskHandlers.Create(new CreateTaskRequest("Ecrire les tests", "Phase 2"), db.Context);

        var created = Assert.IsType<Created<TaskItem>>(result);
        Assert.Equal($"/tasks/{created.Value!.Id}", created.Location);
        Assert.Equal(1, await db.Context.Tasks.CountAsync());
    }

    [Fact]
    public async Task Create_always_starts_a_task_in_todo()
    {
        using var db = new TestDatabase();

        var result = await TaskHandlers.Create(new CreateTaskRequest("Ecrire les tests", null), db.Context);

        // Le statut n'est pas dans CreateTaskRequest : il est impose par le serveur.
        // Laisser le client creer une tache directement en Done viderait le cycle de vie
        // de son sens.
        var created = Assert.IsType<Created<TaskItem>>(result);
        Assert.Equal(TaskItemStatus.Todo, created.Value!.Status);
    }

    [Fact]
    public async Task Create_trims_the_surrounding_whitespace()
    {
        using var db = new TestDatabase();

        var result = await TaskHandlers.Create(new CreateTaskRequest("  Titre  ", "  Corps  "), db.Context);

        var created = Assert.IsType<Created<TaskItem>>(result);
        Assert.Equal("Titre", created.Value!.Title);
        Assert.Equal("Corps", created.Value.Description);
    }

    [Fact]
    public async Task Create_rejects_an_invalid_payload_without_touching_the_database()
    {
        using var db = new TestDatabase();

        var result = await TaskHandlers.Create(new CreateTaskRequest("   ", null), db.Context);

        AssertValidationProblem(result, "title");
        Assert.Equal(0, await db.Context.Tasks.CountAsync());
    }

    [Fact]
    public async Task Update_applies_the_new_status()
    {
        using var db = new TestDatabase();
        var seeded = db.Seed("Tache", TaskItemStatus.Todo);

        var result = await TaskHandlers.Update(
            seeded.Id, new UpdateTaskRequest("Tache", null, TaskItemStatus.Done), db.Context);

        var ok = Assert.IsType<Ok<TaskItem>>(result);
        Assert.Equal(TaskItemStatus.Done, ok.Value!.Status);
    }

    // Toute transition est permise, y compris le retour en arriere : une tache passee
    // en Done par erreur doit pouvoir revenir en InProgress sans passer par la base.
    [Theory]
    [InlineData(TaskItemStatus.Todo, TaskItemStatus.InProgress)]
    [InlineData(TaskItemStatus.InProgress, TaskItemStatus.Done)]
    [InlineData(TaskItemStatus.Done, TaskItemStatus.InProgress)]
    [InlineData(TaskItemStatus.Done, TaskItemStatus.Todo)]
    public async Task Update_allows_any_transition_between_declared_statuses(
        TaskItemStatus from, TaskItemStatus to)
    {
        using var db = new TestDatabase();
        var seeded = db.Seed("Tache", from);

        var result = await TaskHandlers.Update(
            seeded.Id, new UpdateTaskRequest("Tache", null, to), db.Context);

        var ok = Assert.IsType<Ok<TaskItem>>(result);
        Assert.Equal(to, ok.Value!.Status);
    }

    [Fact]
    public async Task Update_keeps_the_current_status_when_the_payload_omits_it()
    {
        using var db = new TestDatabase();
        var seeded = db.Seed("Tache", TaskItemStatus.InProgress);

        var result = await TaskHandlers.Update(
            seeded.Id, new UpdateTaskRequest("Titre corrige", null, Status: null), db.Context);

        // Le piege : un PUT sans statut ne doit pas retomber sur Todo par defaut,
        // sinon renommer une tache en cours la ferait regresser silencieusement.
        var ok = Assert.IsType<Ok<TaskItem>>(result);
        Assert.Equal(TaskItemStatus.InProgress, ok.Value!.Status);
        Assert.Equal("Titre corrige", ok.Value.Title);
    }

    [Fact]
    public async Task Update_rejects_a_status_outside_the_enum()
    {
        using var db = new TestDatabase();
        var seeded = db.Seed("Tache", TaskItemStatus.Todo);

        var result = await TaskHandlers.Update(
            seeded.Id, new UpdateTaskRequest("Tache", null, (TaskItemStatus)99), db.Context);

        AssertValidationProblem(result, "status");
        Assert.Equal(TaskItemStatus.Todo, (await db.Context.Tasks.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Update_validates_before_looking_the_task_up()
    {
        using var db = new TestDatabase();

        var result = await TaskHandlers.Update(
            Guid.NewGuid(), new UpdateTaskRequest(null, null, null), db.Context);

        // Payload invalide sur un identifiant inconnu : c'est 400, pas 404. Le serveur
        // ne peut pas affirmer que la ressource est absente s'il n'a pas compris la requete.
        AssertValidationProblem(result, "title");
    }

    [Fact]
    public async Task Update_returns_404_when_the_identifier_is_unknown()
    {
        using var db = new TestDatabase();

        var result = await TaskHandlers.Update(
            Guid.NewGuid(), new UpdateTaskRequest("Tache", null, null), db.Context);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
    }

    [Fact]
    public async Task Delete_removes_the_task_and_returns_204()
    {
        using var db = new TestDatabase();
        var seeded = db.Seed();

        var result = await TaskHandlers.Delete(seeded.Id, db.Context);

        Assert.IsType<NoContent>(result);
        Assert.Equal(0, await db.Context.Tasks.CountAsync());
    }

    [Fact]
    public async Task Delete_returns_404_when_the_identifier_is_unknown()
    {
        using var db = new TestDatabase();

        var result = await TaskHandlers.Delete(Guid.NewGuid(), db.Context);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
    }

    // Results.ValidationProblem ne renvoie pas le type ValidationProblem mais un
    // ProblemHttpResult portant un HttpValidationProblemDetails -- seul TypedResults
    // expose le type nomme. Assertion sur le contrat observe par le client : le code
    // 400 et le detail des champs fautifs.
    private static void AssertValidationProblem(IResult result, params string[] expectedKeys)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);

        var details = Assert.IsType<HttpValidationProblemDetails>(problem.ProblemDetails);
        Assert.All(expectedKeys, key => Assert.Contains(key, details.Errors.Keys));
    }
}
