using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TaskApi.Data;

// La sonde ready repond "puis-je servir du trafic ?". Tant que la migration etait appliquee
// au demarrage, une base joignable suffisait a le garantir. Depuis qu'elle est sortie dans
// un Job, ce n'est plus vrai : un pod peut joindre une base dont le schema n'existe pas
// encore, et il repondrait alors 500 sur chaque requete.
//
// Le controle porte donc sur les deux conditions : la base repond, et le schema attendu par
// cette version du binaire y est deja applique. Effet voulu, et c'est ce qui resout
// l'ordonnancement Job/Deployment : les replicas restent hors des endpoints du Service tant
// que le Job n'a pas fini, sans qu'aucun des deux objets n'ait besoin d'attendre l'autre.
//
// Une exception (base injoignable) n'est pas rattrapee ici : HealthCheckService la convertit
// en Unhealthy, ce qui est exactement le resultat voulu.
public sealed class SchemaReadyHealthCheck(AppDbContext database) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var pending = (await database.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

        return pending.Count == 0
            ? HealthCheckResult.Healthy("Base joignable, schema a jour.")
            : HealthCheckResult.Unhealthy(
                $"Migrations non appliquees : {string.Join(", ", pending)}.");
    }
}
