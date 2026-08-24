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

// Transforme les exceptions non gerees et les codes d'erreur en reponses ProblemDetails
// (RFC 9457) au lieu de corps vides.
builder.Services.AddProblemDetails();

builder.Services.AddOpenApi();

// Deux sondes distinctes, deux questions differentes :
//   live  -- le process repond-il ? une reponse negative doit entrainer un redemarrage.
//   ready -- peut-il servir du trafic ? la base est une dependance, pas le process.
// Confondre les deux fait redemarrer l'application en boucle quand la base tombe.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("postgres", tags: ["ready"]);

var app = builder.Build();

// Migration au demarrage : acceptable ici car un seul writer. Avec plusieurs replicas
// (phase 6), deux pods migreraient en concurrence -- a sortir dans un Job dedie.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
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
