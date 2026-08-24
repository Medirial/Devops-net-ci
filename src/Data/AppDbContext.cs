using Microsoft.EntityFrameworkCore;
using TaskApi.Models;

namespace TaskApi.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var task = modelBuilder.Entity<TaskItem>();

        task.HasKey(t => t.Id);
        task.Property(t => t.Title).IsRequired().HasMaxLength(200);
        task.Property(t => t.Description).HasMaxLength(2000);
        task.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);

        // Les listes sont triees par date decroissante : sans index, chaque GET declenche
        // un tri complet de la table.
        task.HasIndex(t => t.CreatedAt);
    }
}
