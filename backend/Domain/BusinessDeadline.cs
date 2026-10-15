namespace Domain;

public static class BusinessDeadline
{
    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public static DateTimeOffset Calculate(DateTimeOffset openedAtUtc, RequestPriority priority)
    {
        if (openedAtUtc.Offset != TimeSpan.Zero)
            throw new DomainRuleException("invalid_opening_time", "Abertura deve usar instante UTC.");

        var businessDays = priority switch
        {
            RequestPriority.High => 1,
            RequestPriority.Medium => 3,
            RequestPriority.Low => 5,
            _ => throw new DomainRuleException("invalid_priority", "Prioridade inválida.")
        };

        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(openedAtUtc, SaoPaulo).DateTime);
        while (businessDays > 0)
        {
            localDate = localDate.AddDays(1);
            if (localDate.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                businessDays--;
        }

        var localDeadline = localDate.ToDateTime(new TimeOnly(23, 59, 59, 999), DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localDeadline, SaoPaulo), TimeSpan.Zero);
    }
}
