namespace Domain;

public sealed class Interaction
{
    private Interaction() { }

    public Interaction(Guid id, Guid requestId, Guid authorId, string text, DateTimeOffset createdAtUtc)
    {
        Id = id;
        RequestId = requestId;
        AuthorId = authorId;
        Text = text;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid RequestId { get; private set; }
    public ServiceRequest Request { get; private set; } = null!;
    public Guid AuthorId { get; private set; }
    public DemoUser Author { get; private set; } = null!;
    public string Text { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
}
