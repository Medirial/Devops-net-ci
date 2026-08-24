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

var app = builder.Build();

// Migration au demarrage : acceptable ici car un seul writer. Avec plusieurs replicas
// (phase 6), deux pods migreraient en concurrence -- a sortir dans un Job dedie.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapTaskEndpoints();

app.Run();
