using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public sealed class DemoDbContext(DbContextOptions<DemoDbContext> options) : DbContext(options)
{
    public DbSet<DemoUserRow> Users => Set<DemoUserRow>();
    public DbSet<DemoClientRow> Clients => Set<DemoClientRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DemoUserRow>(entity =>
        {
            entity.ToTable("demo_users");
            entity.HasKey(user => user.Id);
            entity.Property(user => user.DisplayName).HasMaxLength(100).IsRequired();
            entity.Property(user => user.Role).HasMaxLength(20).IsRequired();
        });

        modelBuilder.Entity<DemoClientRow>(entity =>
        {
            entity.ToTable("demo_clients");
            entity.HasKey(client => client.Id);
            entity.Property(client => client.DisplayName).HasMaxLength(100).IsRequired();
        });
    }
}

public sealed class DemoUserRow
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

public sealed class DemoClientRow
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}
