namespace TaskApi.Data;

// Le schema doit arriver en base d'une facon differente selon la facon dont l'application
// est deployee. Un seul processus peut migrer puis servir ; plusieurs replicas ne le
// peuvent pas -- deux pods appliqueraient la meme migration en meme temps sur la meme base.
public enum MigrationMode
{
    // Migre puis sert le trafic. Cas du poste de developpement et de Compose : un seul
    // processus ecrit, la course n'existe pas.
    Startup,

    // Ne migre pas. Cas des replicas Kubernetes : le schema est la responsabilite du Job.
    None,

    // Migre et sort. Cas du Job Kubernetes : le meme binaire, sans serveur HTTP.
    Only
}

public static class MigrationModes
{
    // La cle est lue par le fournisseur de configuration d'environnement sous la forme
    // Database__MigrationMode -- meme mecanisme que ConnectionStrings__Postgres.
    public const string ConfigurationKey = "Database:MigrationMode";

    // Valeur absente = Startup : le comportement d'avant la sortie des migrations reste
    // celui par defaut, donc un poste de developpement et un docker compose up n'ont
    // rien a configurer. C'est le deploiement multi-replicas qui est l'exception, et il
    // le declare explicitement dans son ConfigMap.
    public static MigrationMode Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return MigrationMode.Startup;
        }

        // Une valeur inconnue n'est pas ramenee au defaut : une faute de frappe dans un
        // ConfigMap donnerait alors des replicas qui migrent en concurrence sans le dire.
        // Mieux vaut un conteneur qui refuse de demarrer.
        return Enum.TryParse<MigrationMode>(value.Trim(), ignoreCase: true, out var mode)
            ? mode
            : throw new InvalidOperationException(
                $"Valeur invalide pour {ConfigurationKey} : '{value}'. " +
                $"Attendu : {string.Join(", ", Enum.GetNames<MigrationMode>()).ToLowerInvariant()}.");
    }
}
