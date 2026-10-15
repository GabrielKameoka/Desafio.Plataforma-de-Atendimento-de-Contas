namespace Domain;

public enum RequestType { FeeDispute }
public enum RequestStatus { Open, UnderReview, WaitingForCustomer, Resolved }
public enum RequestPriority { Low, Medium, High }

public sealed class ServiceRequest
{
    private readonly List<Interaction> _interactions = [];
    private readonly List<AuditEvent> _auditEvents = [];

    private ServiceRequest() { }

    public Guid Id { get; private set; }
    public string Protocol { get; private set; } = string.Empty;
    public Guid ClientId { get; private set; }
    public Client Client { get; private set; } = null!;
    public RequestType Type { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public RequestPriority Priority { get; private set; }
    public RequestStatus Status { get; private set; }
    public Guid AssigneeId { get; private set; }
    public DemoUser Assignee { get; private set; } = null!;
    public DateTimeOffset OpenedAtUtc { get; private set; }
    public DateTimeOffset DeadlineAtUtc { get; private set; }
    public long Version { get; private set; }
    public IReadOnlyCollection<Interaction> Interactions => _interactions.AsReadOnly();
    public IReadOnlyCollection<AuditEvent> AuditEvents => _auditEvents.AsReadOnly();

    public static ServiceRequest OpenFeeDispute(Guid id, string protocol, Client? client,
        DemoUser? attendant, string? description, string? idempotencyKey, TimeProvider clock)
    {
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(protocol) || protocol.Trim().Length > 40)
            throw new DomainRuleException("invalid_request_identity", "Identificador e protocolo de até 40 caracteres são obrigatórios.");
        if (client is null || client.Id == Guid.Empty)
            throw new DomainRuleException("client_required", "Cliente válido é obrigatório.");
        if (attendant is null || attendant.Id == Guid.Empty || !attendant.IsActive || attendant.Role != DemoRole.Attendant)
            throw new DomainRuleException("attendant_required", "Atendente ativo é obrigatório.");
        if (string.IsNullOrWhiteSpace(description) || description.Trim().Length > 4000)
            throw new DomainRuleException("description_required", "Descrição deve conter de 1 a 4000 caracteres.");
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Trim().Length > 120)
            throw new DomainRuleException("idempotency_key_required", "Chave de idempotência deve conter de 1 a 120 caracteres.");
        ArgumentNullException.ThrowIfNull(clock);

        var openedAtUtc = clock.GetUtcNow();
        var request = new ServiceRequest
        {
            Id = id,
            Protocol = protocol.Trim(),
            ClientId = client.Id,
            Client = client,
            Type = RequestType.FeeDispute,
            Description = description.Trim(),
            Priority = RequestPriority.Medium,
            Status = RequestStatus.Open,
            AssigneeId = attendant.Id,
            Assignee = attendant,
            OpenedAtUtc = openedAtUtc,
            DeadlineAtUtc = BusinessDeadline.Calculate(openedAtUtc, RequestPriority.Medium),
            Version = 1
        };
        request._auditEvents.Add(new AuditEvent(Guid.NewGuid(), id, AuditEventType.Opened,
            null, RequestStatus.Open.ToString(), attendant.Id, "Abertura da solicitação", openedAtUtc, 1));
        return request;
    }

    public void ChangeStatus(RequestStatus nextStatus, DemoUser? actor, string? reason, TimeProvider clock)
    {
        EnsureActive();
        EnsureActor(actor);
        EnsureReason(reason);
        ArgumentNullException.ThrowIfNull(clock);
        if (!Enum.IsDefined(nextStatus) || nextStatus == RequestStatus.Open ||
            (Status == RequestStatus.Open && nextStatus != RequestStatus.UnderReview) ||
            (Status == RequestStatus.UnderReview && nextStatus is not (RequestStatus.WaitingForCustomer or RequestStatus.Resolved)) ||
            (Status == RequestStatus.WaitingForCustomer && nextStatus != RequestStatus.UnderReview))
            throw new DomainRuleException("invalid_status_transition", "Transição de status não permitida.");

        var previous = Status;
        Status = nextStatus;
        AddAudit(AuditEventType.StatusChanged, previous.ToString(), nextStatus.ToString(), actor!, reason!, clock);
    }

    public void Reopen(DemoUser? supervisor, string? reason, TimeProvider clock)
    {
        if (Status != RequestStatus.Resolved)
            throw new DomainRuleException("invalid_status_transition", "Somente uma solicitação resolvida pode ser reaberta.");
        EnsureActor(supervisor);
        if (supervisor!.Role != DemoRole.Supervisor)
            throw new DomainRuleException("supervisor_required", "A reabertura exige supervisão.");
        EnsureReason(reason);
        ArgumentNullException.ThrowIfNull(clock);

        Status = RequestStatus.UnderReview;
        AddAudit(AuditEventType.Reopened, RequestStatus.Resolved.ToString(), Status.ToString(), supervisor, reason!, clock);
    }

    public void ChangePriority(RequestPriority priority, DemoUser? actor, string? reason, TimeProvider clock)
    {
        EnsureActive();
        EnsureActor(actor);
        EnsureReason(reason);
        ArgumentNullException.ThrowIfNull(clock);
        if (!Enum.IsDefined(priority) || priority == Priority)
            throw new DomainRuleException("invalid_priority", "Informe uma prioridade diferente e válida.");

        var previous = Priority;
        Priority = priority;
        AddAudit(AuditEventType.PriorityChanged, previous.ToString(), priority.ToString(), actor!, reason!, clock);
    }

    public void AssignTo(DemoUser? assignee, DemoUser? actor, string? reason, TimeProvider clock)
    {
        EnsureActive();
        EnsureActor(actor);
        EnsureReason(reason);
        ArgumentNullException.ThrowIfNull(clock);
        if (assignee is null || assignee.Id == Guid.Empty || !assignee.IsActive || assignee.Role != DemoRole.Attendant)
            throw new DomainRuleException("invalid_assignee", "Responsável deve ser um atendente ativo.");
        if (assignee.Id == AssigneeId)
            throw new DomainRuleException("same_assignee", "Escolha outro responsável.");

        var previous = AssigneeId;
        AssigneeId = assignee.Id;
        Assignee = assignee;
        AddAudit(AuditEventType.AssigneeChanged, previous.ToString(), assignee.Id.ToString(), actor!, reason!, clock);
    }

    public void ChangeDeadline(DateTimeOffset deadlineAtUtc, DemoUser? actor, string? reason, TimeProvider clock)
    {
        EnsureActive();
        EnsureActor(actor);
        EnsureReason(reason);
        ArgumentNullException.ThrowIfNull(clock);
        if (deadlineAtUtc.Offset != TimeSpan.Zero || deadlineAtUtc < OpenedAtUtc || deadlineAtUtc == DeadlineAtUtc)
            throw new DomainRuleException("invalid_deadline", "Prazo deve ser UTC, diferente do atual e posterior à abertura.");

        var previous = DeadlineAtUtc;
        DeadlineAtUtc = deadlineAtUtc;
        AddAudit(AuditEventType.DeadlineChanged, previous.ToString("O"), deadlineAtUtc.ToString("O"), actor!, reason!, clock);
    }

    public Interaction AddInteraction(Guid interactionId, DemoUser? author, string? text, TimeProvider clock)
    {
        EnsureActive();
        EnsureActor(author);
        ArgumentNullException.ThrowIfNull(clock);
        if (interactionId == Guid.Empty || string.IsNullOrWhiteSpace(text) || text.Trim().Length > 4000)
            throw new DomainRuleException("invalid_interaction", "Identificador e texto da interação de até 4000 caracteres são obrigatórios.");

        var interaction = new Interaction(interactionId, Id, author!.Id, text.Trim(), clock.GetUtcNow());
        _interactions.Add(interaction);
        Version++;
        return interaction;
    }

    public bool IsDueSoon(DateTimeOffset nowUtc) =>
        Status != RequestStatus.Resolved && nowUtc <= DeadlineAtUtc && DeadlineAtUtc - nowUtc <= TimeSpan.FromHours(24);

    public bool IsOverdue(DateTimeOffset nowUtc) =>
        Status != RequestStatus.Resolved && nowUtc > DeadlineAtUtc;

    private void AddAudit(AuditEventType type, string? previousValue, string? newValue,
        DemoUser actor, string reason, TimeProvider clock)
    {
        Version++;
        _auditEvents.Add(new AuditEvent(Guid.NewGuid(), Id, type, previousValue, newValue,
            actor.Id, reason.Trim(), clock.GetUtcNow(), Version));
    }

    private void EnsureActive()
    {
        if (Status == RequestStatus.Resolved)
            throw new DomainRuleException("request_resolved", "Solicitação resolvida deve ser reaberta antes de alterações.");
    }

    private static void EnsureActor(DemoUser? actor)
    {
        if (actor is null || actor.Id == Guid.Empty || !actor.IsActive)
            throw new DomainRuleException("invalid_actor", "Usuário ativo é obrigatório.");
    }

    private static void EnsureReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 1000)
            throw new DomainRuleException("reason_required", "Motivo deve conter de 1 a 1000 caracteres.");
    }
}
