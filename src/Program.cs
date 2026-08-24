using Microsoft.EntityFrameworkCore;
using TaskApi.Data;

var builder = WebApplication.CreateBuilder(args);

// Echoue au demarrage plutot qu'a la premiere requete : un conteneur mal configure
// doit mourir tout de suite, pas servir des 500.
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException(
        "Chaine de connexion absente. Definir ConnectionStrings__Postgres.");

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

var app = builder.Build();

app.Run();
