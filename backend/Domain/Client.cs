namespace Domain;

public sealed class Client
{
    private Client() { }

    public Client(Guid id, string displayName)
    {
        Id = id;
        DisplayName = displayName;
    }

    public Guid Id { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
}
