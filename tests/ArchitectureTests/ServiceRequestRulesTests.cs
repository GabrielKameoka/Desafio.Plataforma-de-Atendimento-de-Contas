using Domain;

namespace ArchitectureTests;

public sealed class ServiceRequestRulesTests
{
    private static readonly DateTimeOffset FridayUtc = new(2026, 10, 9, 14, 0, 0, TimeSpan.Zero);
    private static readonly Client Client = new(Guid.NewGuid(), "Cliente fictício");
    private static readonly DemoUser Attendant = new(Guid.NewGuid(), "Atendente fictício", DemoRole.Attendant);
    private static readonly DemoUser OtherAttendant = new(Guid.NewGuid(), "Outro atendente", DemoRole.Attendant);
    private static readonly DemoUser Supervisor = new(Guid.NewGuid(), "Supervisora", DemoRole.Supervisor);
    private static readonly TimeProvider Clock = new FixedClock(FridayUtc);

    [Fact]
    public void Opening_sets_initial_state_deadline_and_audit()
    {
        var request = Open();

        Assert.Equal(RequestType.FeeDispute, request.Type);
        Assert.Equal(RequestPriority.Medium, request.Priority);
        Assert.Equal(RequestStatus.Open, request.Status);
        Assert.Equal(Attendant.Id, request.AssigneeId);
        Assert.Equal(1, request.Version);
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 2, 59, 59, 999, TimeSpan.Zero), request.DeadlineAtUtc);
        var opened = Assert.Single(request.AuditEvents);
        Assert.Equal(AuditEventType.Opened, opened.Type);
        Assert.Equal(Attendant.Id, opened.AuthorId);
        Assert.Equal(1, opened.ResultingVersion);
    }

    [Fact]
    public void Opening_rejects_missing_client_description_key_and_inactive_or_non_attendant_user()
    {
        Assert.Equal("client_required", Rule(() => ServiceRequest.OpenFeeDispute(Guid.NewGuid(), "AT-1", null, Attendant, "Contestação", "key", Clock)).Code);
        Assert.Equal("description_required", Rule(() => ServiceRequest.OpenFeeDispute(Guid.NewGuid(), "AT-1", Client, Attendant, "  ", "key", Clock)).Code);
        Assert.Equal("idempotency_key_required", Rule(() => ServiceRequest.OpenFeeDispute(Guid.NewGuid(), "AT-1", Client, Attendant, "Contestação", " ", Clock)).Code);
        Assert.Equal("attendant_required", Rule(() => ServiceRequest.OpenFeeDispute(Guid.NewGuid(), "AT-1", Client, Supervisor, "Contestação", "key", Clock)).Code);
        Assert.Equal("attendant_required", Rule(() => ServiceRequest.OpenFeeDispute(Guid.NewGuid(), "AT-1", Client,
            new DemoUser(Guid.NewGuid(), "Inativo", DemoRole.Attendant, false), "Contestação", "key", Clock)).Code);
    }

    [Fact]
    public void Valid_status_path_records_each_change_and_reopening_requires_supervisor()
    {
        var request = Open();
        request.ChangeStatus(RequestStatus.UnderReview, Attendant, "Iniciar análise", Clock);
        request.ChangeStatus(RequestStatus.WaitingForCustomer, Attendant, "Solicitar dados", Clock);
        request.ChangeStatus(RequestStatus.UnderReview, OtherAttendant, "Dados recebidos", Clock);
        request.ChangeStatus(RequestStatus.Resolved, OtherAttendant, "Análise concluída", Clock);

        Assert.Equal(RequestStatus.Resolved, request.Status);
        Assert.Equal(5, request.Version);
        Assert.Equal(5, request.AuditEvents.Count);
        Assert.Equal("supervisor_required", Rule(() => request.Reopen(Attendant, "Revisar", Clock)).Code);
        Assert.Equal("reason_required", Rule(() => request.Reopen(Supervisor, " ", Clock)).Code);
        Assert.Equal(5, request.Version);
        request.Reopen(Supervisor, "Revisão necessária", Clock);
        Assert.Equal(RequestStatus.UnderReview, request.Status);
        Assert.Equal(6, request.Version);
        Assert.Equal(AuditEventType.Reopened, request.AuditEvents.Last().Type);
        Assert.Equal(Supervisor.Id, request.AuditEvents.Last().AuthorId);
    }

    [Fact]
    public void Invalid_transition_and_missing_reason_leave_state_and_history_unchanged()
    {
        var request = Open();
        Assert.Equal("invalid_status_transition", Rule(() => request.ChangeStatus(RequestStatus.Resolved, Attendant, "Pular análise", Clock)).Code);
        Assert.Equal("reason_required", Rule(() => request.ChangeStatus(RequestStatus.UnderReview, Attendant, " ", Clock)).Code);
        Assert.Equal(RequestStatus.Open, request.Status);
        Assert.Equal(1, request.Version);
        Assert.Single(request.AuditEvents);

        request.ChangeStatus(RequestStatus.UnderReview, Attendant, "Analisar", Clock);
        Assert.Equal("invalid_status_transition", Rule(() => request.ChangeStatus(RequestStatus.Open, Attendant, "Voltar", Clock)).Code);
        Assert.Equal("reason_required", Rule(() => request.ChangeStatus(RequestStatus.Resolved, Attendant, null, Clock)).Code);
        request.ChangeStatus(RequestStatus.WaitingForCustomer, Attendant, "Solicitar dados", Clock);
        Assert.Equal("invalid_status_transition", Rule(() => request.ChangeStatus(RequestStatus.Resolved, Attendant, "Pular análise", Clock)).Code);
        Assert.Equal(3, request.Version);
        Assert.Equal(3, request.AuditEvents.Count);
    }

    [Fact]
    public void Resolved_request_blocks_changes_and_interactions_until_reopened()
    {
        var request = Open();
        request.ChangeStatus(RequestStatus.UnderReview, Attendant, "Analisar", Clock);
        request.ChangeStatus(RequestStatus.Resolved, Attendant, "Concluído", Clock);
        var version = request.Version;
        var auditCount = request.AuditEvents.Count;

        Assert.Equal("request_resolved", Rule(() => request.ChangeStatus(RequestStatus.UnderReview, Attendant, "Voltar", Clock)).Code);
        Assert.Equal("request_resolved", Rule(() => request.ChangePriority(RequestPriority.High, Attendant, "Urgente", Clock)).Code);
        Assert.Equal("request_resolved", Rule(() => request.AssignTo(OtherAttendant, Attendant, "Transferir", Clock)).Code);
        Assert.Equal("request_resolved", Rule(() => request.ChangeDeadline(request.DeadlineAtUtc.AddDays(1), Attendant, "Prorrogar", Clock)).Code);
        Assert.Equal("request_resolved", Rule(() => request.AddInteraction(Guid.NewGuid(), Attendant, "Contato", Clock)).Code);
        Assert.Equal(version, request.Version);
        Assert.Equal(auditCount, request.AuditEvents.Count);
        Assert.Empty(request.Interactions);

        request.Reopen(Supervisor, "Revisar", Clock);
        request.AddInteraction(Guid.NewGuid(), Attendant, "Novo contato", Clock);
        Assert.Single(request.Interactions);
    }

    [Fact]
    public void Priority_change_does_not_recalculate_deadline_and_deadline_change_is_separate()
    {
        var request = Open();
        var originalDeadline = request.DeadlineAtUtc;
        request.ChangePriority(RequestPriority.High, Attendant, "Acelerar", Clock);
        Assert.Equal(originalDeadline, request.DeadlineAtUtc);
        Assert.Equal(AuditEventType.PriorityChanged, request.AuditEvents.Last().Type);

        Assert.Equal("invalid_deadline", Rule(() => request.ChangeDeadline(FridayUtc.AddMinutes(-1), Attendant, "Antecipar", Clock)).Code);
        Assert.Equal("reason_required", Rule(() => request.ChangeDeadline(originalDeadline.AddDays(1), Attendant, "", Clock)).Code);
        request.ChangeDeadline(originalDeadline.AddDays(1), Attendant, "Prorrogar", Clock);
        Assert.Equal(originalDeadline.AddDays(1), request.DeadlineAtUtc);
        Assert.Equal(AuditEventType.DeadlineChanged, request.AuditEvents.Last().Type);
        Assert.Equal(3, request.Version);
    }

    [Fact]
    public void Assignment_requires_active_attendant_and_records_previous_and_new_values()
    {
        var request = Open();
        Assert.Equal("invalid_assignee", Rule(() => request.AssignTo(Supervisor, Attendant, "Transferir", Clock)).Code);
        Assert.Equal("same_assignee", Rule(() => request.AssignTo(Attendant, Attendant, "Assumir", Clock)).Code);
        request.AssignTo(OtherAttendant, Attendant, "Transferir", Clock);
        Assert.Equal(OtherAttendant.Id, request.AssigneeId);
        var audit = request.AuditEvents.Last();
        Assert.Equal(Attendant.Id.ToString(), audit.PreviousValue);
        Assert.Equal(OtherAttendant.Id.ToString(), audit.NewValue);
        Assert.Equal("Transferir", audit.Reason);
    }

    [Fact]
    public void Waiting_for_customer_remains_overdue_and_resolved_is_never_due()
    {
        var request = Open();
        request.ChangeStatus(RequestStatus.UnderReview, Attendant, "Analisar", Clock);
        request.ChangeStatus(RequestStatus.WaitingForCustomer, Attendant, "Solicitar dados", Clock);
        Assert.True(request.IsDueSoon(request.DeadlineAtUtc.AddHours(-23)));
        Assert.True(request.IsOverdue(request.DeadlineAtUtc.AddMilliseconds(1)));
        request.ChangeStatus(RequestStatus.UnderReview, Attendant, "Retomar", Clock);
        request.ChangeStatus(RequestStatus.Resolved, Attendant, "Concluído", Clock);
        Assert.False(request.IsOverdue(request.DeadlineAtUtc.AddDays(1)));
    }

    private static ServiceRequest Open() =>
        ServiceRequest.OpenFeeDispute(Guid.NewGuid(), "AT-2026-001", Client,
            Attendant, "Contestação fictícia", "tentativa-1", Clock);

    private static DomainRuleException Rule(Action action) => Assert.Throws<DomainRuleException>(action);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
