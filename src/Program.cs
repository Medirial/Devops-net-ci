using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using TaskApi.Data;
using TaskApi.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// Echoue au demarrage plutot qu'a la premiere requete : un conteneur mal configure
// doit mourir tout de suite, pas servir des 500.
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException(
        "Chaine de connexion absente. Definir ConnectionStrings__Postgres.");

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

// Lu avant la construction de l'hote : une valeur invalide doit faire echouer le demarrage,
// pas etre decouverte apres que le serveur a ouvert son port.
var migrationMode = MigrationModes.Parse(builder.Configuration[MigrationModes.ConfigurationKey]);

// Transforme les exceptions non gerees et les codes d'erreur en reponses ProblemDetails
// (RFC 9457) au lieu de corps vides.
builder.Services.AddProblemDetails();

builder.Services.AddOpenApi();

// Deux sondes distinctes, deux questions differentes :
//   live  -- le process repond-il ? une reponse negative doit entrainer un redemarrage.
//   ready -- peut-il servir du trafic ? la base est une dependance, pas le process.
// Confondre les deux fait redemarrer l'application en boucle quand la base tombe.
builder.Services.AddHealthChecks()
    .AddCheck<SchemaReadyHealthCheck>("postgres", tags: ["ready"]);

var app = builder.Build();

// La migration n'est plus inconditionnelle : avec deux replicas, deux pods l'appliqueraient
// en concurrence sur la meme base au premier demarrage. Le mode par defaut reste Startup
// pour le poste de developpement et Compose, ou un seul processus ecrit.
if (migrationMode is not MigrationMode.None)
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

// Le Job Kubernetes lance le meme binaire avec Database__MigrationMode=only. Sans ce retour,
// il ouvrirait un port HTTP et ne se terminerait jamais : le Job resterait en cours pour
// toujours au lieu de passer en Completed.
if (migrationMode is MigrationMode.Only)
{
    return;
}

// Document expose hors production seulement : il decrit toute la surface de l'API,
// c'est une carte offerte a qui cherche une faille.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapTaskEndpoints();

app.Run();
