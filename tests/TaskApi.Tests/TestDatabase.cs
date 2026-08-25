using Microsoft.EntityFrameworkCore;
using TaskApi.Data;
using TaskApi.Models;

namespace TaskApi.Tests;

// Provider InMemory, apres avoir essaye SQLite en memoire.
//
// SQLite est plus proche d'une vraie base -- il applique les types et les contraintes --
// mais il refuse ORDER BY sur une colonne DateTimeOffset, la que PostgreSQL l'accepte.
// Le tri de GET /tasks echouait donc en test alors qu'il fonctionne en production :
// un echec du provider, pas du code. Un test qui ment dans ce sens est pire que pas de test.
//
// Contrepartie assumee : InMemory n'est pas relationnel, il ne verifie ni les longueurs
// ni les cles. Ce n'est pas son role ici -- ces tests portent sur la logique des handlers.
// Le schema, lui, est valide par la migration reelle appliquee au demarrage.
internal sealed class TestDatabase : IDisposable
{
    public AppDbContext Context { get; }

    public TestDatabase()
    {
        // Un nom de base par instance : les tests xUnit d'une meme classe tournent en
        // sequence mais les classes tournent en parallele. Un nom partage les ferait
        // lire les donnees des autres.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        Context = new AppDbContext(options);
    }

    public TaskItem Seed(
        string title = "Tache",
        TaskItemStatus status = TaskItemStatus.Todo,
        DateTimeOffset? createdAt = null)
    {
        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            Title = title,
            Status = status,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow
        };

        Context.Tasks.Add(task);
        Context.SaveChanges();

        // Sans ce detachement, l'entite reste suivie : un handler qui la recharge
        // recevrait l'instance en cache au lieu de relire la base, et une assertion
        // sur une valeur non persistee passerait quand meme.
        Context.Entry(task).State = EntityState.Detached;

        return task;
    }

    public void Dispose() => Context.Dispose();
}
