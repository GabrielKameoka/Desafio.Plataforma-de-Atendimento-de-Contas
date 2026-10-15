using Domain;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArchitectureTests;

public sealed class PersistenceMappingTests
{
    [Fact]
    public void Ef_tracks_opening_audit_through_read_only_navigation()
    {
        var options = new DbContextOptionsBuilder<DemoDbContext>()
            .UseNpgsql("Host=localhost;Database=not_used;Username=not_used;Password=not_used")
            .Options;
        using var db = new DemoDbContext(options);
        var client = new Client(Guid.NewGuid(), "Cliente fictício");
        var attendant = new DemoUser(Guid.NewGuid(), "Atendente fictício", DemoRole.Attendant);
        var request = ServiceRequest.OpenFeeDispute(Guid.NewGuid(), "AT-1", client, attendant,
            "Contestação fictícia", "attempt-1", TimeProvider.System);

        db.Requests.Add(request);
        request.AddInteraction(Guid.NewGuid(), attendant, "Contato fictício", TimeProvider.System);
        db.ChangeTracker.DetectChanges();

        Assert.Single(db.ChangeTracker.Entries<AuditEvent>());
        Assert.Single(db.ChangeTracker.Entries<Interaction>());
        Assert.Equal(EntityState.Added, db.Entry(request).State);
        Assert.Equal(client.Id, request.ClientId);
        Assert.Equal(attendant.Id, request.AssigneeId);
    }
}
