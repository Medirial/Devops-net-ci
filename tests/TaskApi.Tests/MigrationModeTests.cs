using TaskApi.Data;

namespace TaskApi.Tests;

public class MigrationModeTests
{
    [Fact]
    public void Parse_defaults_to_startup_when_nothing_is_configured()
    {
        // Le defaut est ce qui fait tourner make run et docker compose up sans configuration.
        Assert.Equal(MigrationMode.Startup, MigrationModes.Parse(null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_defaults_to_startup_when_the_value_is_blank(string value)
    {
        // Une variable d'environnement declaree mais vide arrive ici comme chaine vide,
        // pas comme null.
        Assert.Equal(MigrationMode.Startup, MigrationModes.Parse(value));
    }

    [Theory]
    [InlineData("none", MigrationMode.None)]
    [InlineData("None", MigrationMode.None)]
    [InlineData("ONLY", MigrationMode.Only)]
    [InlineData(" startup ", MigrationMode.Startup)]
    public void Parse_accepts_the_three_modes_whatever_the_casing(string value, MigrationMode expected)
    {
        // La valeur vient d'un ConfigMap ecrit a la main : la casse et les espaces de bord
        // ne doivent pas changer le comportement.
        Assert.Equal(expected, MigrationModes.Parse(value));
    }

    [Fact]
    public void Parse_rejects_an_unknown_value()
    {
        // Point important : une faute de frappe ne doit pas retomber silencieusement sur
        // Startup, sinon les deux replicas migreraient en concurrence sans que rien ne le
        // signale.
        var exception = Assert.Throws<InvalidOperationException>(() => MigrationModes.Parse("nonw"));

        Assert.Contains("Database:MigrationMode", exception.Message);
    }
}
