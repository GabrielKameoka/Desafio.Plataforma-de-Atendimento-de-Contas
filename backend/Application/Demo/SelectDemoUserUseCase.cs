using Domain;

namespace Application.Demo;

public interface IDemoUserRepository
{
    Task<Domain.DemoUser?> FindActiveAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class SelectDemoUserUseCase(IDemoUserRepository users)
{
    public async Task<SelectedDemoUser?> ExecuteAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            throw new DomainRuleException("invalid_user_id", "Selecione um usuário fictício válido.");

        var user = await users.FindActiveAsync(userId, cancellationToken);
        return user is null ? null : new SelectedDemoUser(user.Id, user.DisplayName, user.Role.ToString());
    }
}

public sealed record SelectedDemoUser(Guid Id, string DisplayName, string Role);
