using Application.Requests;
using Domain;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public sealed class RequestQueueRepository(DemoDbContext db) : IRequestQueueRepository
{
    public async Task<RequestQueuePage> SearchAsync(RequestQueueQuery query, DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var requests = db.Requests.AsNoTracking().AsQueryable();
        if (query.Status is { } status)
            requests = requests.Where(item => item.Status == status);
        if (query.Priority is { } priority)
            requests = requests.Where(item => item.Priority == priority);
        if (query.AssigneeId is { } assigneeId)
            requests = requests.Where(item => item.AssigneeId == assigneeId);
        if (query.Due == DueFilter.DueSoon)
            requests = requests.Where(item => item.Status != RequestStatus.Resolved &&
                item.DeadlineAtUtc >= nowUtc && item.DeadlineAtUtc <= nowUtc.AddHours(24));
        else if (query.Due == DueFilter.Overdue)
            requests = requests.Where(item => item.Status != RequestStatus.Resolved &&
                item.DeadlineAtUtc < nowUtc);

        var total = await requests.CountAsync(cancellationToken);
        var rows = await requests.OrderBy(item => item.DeadlineAtUtc).ThenBy(item => item.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(item => new
            {
                item.Id, item.Protocol, item.ClientId, ClientName = item.Client.DisplayName,
                item.Status, item.Priority, item.AssigneeId, AssigneeName = item.Assignee.DisplayName,
                item.DeadlineAtUtc, item.Version
            }).ToListAsync(cancellationToken);
        var items = rows.Select(item => new RequestQueueItem(item.Id, item.Protocol, item.ClientId,
            item.ClientName, item.Status.ToString(), item.Priority.ToString(), item.AssigneeId,
            item.AssigneeName, item.DeadlineAtUtc, item.Version,
            item.Status == RequestStatus.Resolved ? "Resolved" : item.DeadlineAtUtc < nowUtc
                ? "Overdue" : item.DeadlineAtUtc <= nowUtc.AddHours(24) ? "DueSoon" : "OnTrack"))
            .ToList();
        return new RequestQueuePage(items, total, query.Page, query.PageSize, nowUtc);
    }
}
