using System.Security.Cryptography;
using System.Text;
using Domain;

namespace Application.Requests;

public sealed record OpenRequestResponse(Guid Id, string Protocol, Guid ClientId, string Type,
    string Priority, string Status, Guid AssigneeId, string AssigneeName,
    DateTimeOffset OpenedAtUtc, DateTimeOffset DeadlineAtUtc, long Version);

public sealed record OpenRequestResult(OpenRequestResponse Response, bool WasCreated);

public sealed record StoredOpening(string RequestHash, OpenRequestResponse Response, bool WasCreated);

public interface IRequestOpeningRepository
{
    Task<StoredOpening?> FindByKeyAsync(string key, CancellationToken cancellationToken = default);
    Task<Client?> FindClientAsync(Guid id, CancellationToken cancellationToken = default);
    Task<DemoUser?> FindAttendantAsync(Guid id, CancellationToken cancellationToken = default);
    Task<StoredOpening> CreateOrGetAsync(ServiceRequest request, string key, string requestHash,
        OpenRequestResponse response, CancellationToken cancellationToken = default);
}

public sealed class IdempotencyConflictException : Exception;
public sealed class RequestClientNotFoundException : Exception;
public sealed class AttendantUnavailableException : Exception;

public sealed class OpenFeeDisputeUseCase(IRequestOpeningRepository repository, TimeProvider clock)
{
    public async Task<OpenRequestResult> ExecuteAsync(Guid clientId, Guid attendantId,
        string? description, string? idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (clientId == Guid.Empty)
            throw new DomainRuleException("client_required", "Selecione um cliente válido.");
        if (attendantId == Guid.Empty)
            throw new DomainRuleException("attendant_required", "Sessão de atendente inválida.");
        if (string.IsNullOrWhiteSpace(description) || description.Trim().Length > 4000)
            throw new DomainRuleException("description_required", "Descrição deve conter de 1 a 4000 caracteres.");
        if (string.IsNullOrWhiteSpace(idempotencyKey) ||
            idempotencyKey != idempotencyKey.Trim() || idempotencyKey.Length > 120 ||
            idempotencyKey.Any(char.IsControl))
            throw new DomainRuleException("idempotency_key_required", "Informe uma chave de idempotência válida de até 120 caracteres.");

        var normalizedDescription = description.Trim();
        var requestHash = HashPayload(clientId, normalizedDescription);
        var stored = await repository.FindByKeyAsync(idempotencyKey, cancellationToken);
        if (stored is not null)
            return ReplayOrConflict(stored, requestHash);

        var client = await repository.FindClientAsync(clientId, cancellationToken)
            ?? throw new RequestClientNotFoundException();
        var attendant = await repository.FindAttendantAsync(attendantId, cancellationToken)
            ?? throw new AttendantUnavailableException();
        var request = ServiceRequest.OpenFeeDispute(Guid.NewGuid(), $"ATD-{Guid.NewGuid():N}",
            client, attendant, normalizedDescription, idempotencyKey, clock);
        var response = new OpenRequestResponse(request.Id, request.Protocol, request.ClientId,
            request.Type.ToString(), request.Priority.ToString(), request.Status.ToString(),
            request.AssigneeId, attendant.DisplayName, request.OpenedAtUtc,
            request.DeadlineAtUtc, request.Version);

        stored = await repository.CreateOrGetAsync(request, idempotencyKey, requestHash,
            response, cancellationToken);
        return stored.WasCreated
            ? new OpenRequestResult(stored.Response, true)
            : ReplayOrConflict(stored, requestHash);
    }

    private static OpenRequestResult ReplayOrConflict(StoredOpening stored, string requestHash)
    {
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(stored.RequestHash), Convert.FromHexString(requestHash)))
            throw new IdempotencyConflictException();

        return new OpenRequestResult(stored.Response, false);
    }

    private static string HashPayload(Guid clientId, string description)
    {
        var payload = $"{clientId:N}:{Encoding.UTF8.GetByteCount(description)}:{description}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }
}
