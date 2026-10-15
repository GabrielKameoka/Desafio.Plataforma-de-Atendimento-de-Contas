namespace Application.Demo;

public sealed record DemoUser(Guid Id, string DisplayName, string Role);
public sealed record DemoClient(Guid Id, string DisplayName);
public sealed record DemoCatalog(IReadOnlyList<DemoUser> Users, IReadOnlyList<DemoClient> Clients);

public interface IDemoCatalog
{
    Task<DemoCatalog> GetAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DemoUser>> GetUsersAsync(CancellationToken cancellationToken = default);
}
