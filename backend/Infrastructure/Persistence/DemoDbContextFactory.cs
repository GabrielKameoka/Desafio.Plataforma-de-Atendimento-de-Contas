using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Infrastructure.Persistence;

public sealed class DemoDbContextFactory : IDesignTimeDbContextFactory<DemoDbContext>
{
    public DemoDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<DemoDbContext>()
            .UseNpgsql("Host=localhost;Database=atendimento;Username=atendimento;Password=local_demo_only")
            .Options;
        return new DemoDbContext(options);
    }
}
