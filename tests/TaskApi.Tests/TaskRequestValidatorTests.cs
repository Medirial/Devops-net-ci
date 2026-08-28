using TaskApi.Endpoints;
using TaskApi.Models;

namespace TaskApi.Tests;

public class TaskRequestValidatorTests
{
    [Fact]
    public void Accepts_a_well_formed_request()
    {
        var errors = TaskRequestValidator.Validate(
            "Ecrire les tests", "Phase 2", TaskItemStatus.InProgress, titleRequired: true);

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_missing_title_when_it_is_required(string? title)
    {
        var errors = TaskRequestValidator.Validate(title, null, null, titleRequired: true);

        Assert.Contains("title", errors.Keys);
    }

    [Fact]
    public void Accepts_a_missing_title_when_it_is_optional()
    {
        var errors = TaskRequestValidator.Validate(null, null, null, titleRequired: false);

        Assert.Empty(errors);
    }

    // Les bornes viennent du modele EF : depasser cote API evite une exception PostgreSQL
    // remontee en 500 au lieu d'un 400.
    [Fact]
    public void Rejects_a_title_longer_than_two_hundred_characters()
    {
        var errors = TaskRequestValidator.Validate(new string('a', 201), null, null, titleRequired: true);

        Assert.Contains("title", errors.Keys);
    }

    [Fact]
    public void Accepts_a_title_of_exactly_two_hundred_characters()
    {
        var errors = TaskRequestValidator.Validate(new string('a', 200), null, null, titleRequired: true);

        Assert.Empty(errors);
    }

    [Fact]
    public void Rejects_a_description_longer_than_two_thousand_characters()
    {
        var errors = TaskRequestValidator.Validate("Titre", new string('a', 2001), null, titleRequired: true);

        Assert.Contains("description", errors.Keys);
    }

    [Fact]
    public void Reports_every_broken_rule_at_once()
    {
        var errors = TaskRequestValidator.Validate(
            null, new string('a', 2001), (TaskItemStatus)99, titleRequired: true);

        // Une validation qui s'arrete a la premiere erreur oblige l'appelant a corriger
        // son payload champ par champ, un aller-retour a chaque fois.
        Assert.Equal(3, errors.Count);
    }

    [Theory]
    [InlineData(TaskItemStatus.Todo)]
    [InlineData(TaskItemStatus.InProgress)]
    [InlineData(TaskItemStatus.Done)]
    public void Accepts_every_declared_status(TaskItemStatus status)
    {
        var errors = TaskRequestValidator.Validate("Titre", null, status, titleRequired: true);

        Assert.Empty(errors);
    }

    // Le point du test : System.Text.Json deserialise 99 en TaskItemStatus sans broncher.
    // Sans Enum.IsDefined, la valeur atterrit en base et aucun code ne sait la lire.
    [Theory]
    [InlineData(99)]
    [InlineData(-1)]
    public void Rejects_a_status_outside_the_enum(int status)
    {
        var errors = TaskRequestValidator.Validate("Titre", null, (TaskItemStatus)status, titleRequired: true);

        Assert.Contains("status", errors.Keys);
    }
}
