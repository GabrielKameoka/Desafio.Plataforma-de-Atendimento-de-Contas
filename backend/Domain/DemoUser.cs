namespace Domain;

public enum DemoRole { Attendant, Supervisor }

public sealed class DemoUser
{
    private DemoUser() { }

    public DemoUser(Guid id, string displayName, DemoRole role, bool isActive = true)
    {
        Id = id;
        DisplayName = displayName;
        Role = role;
        IsActive = isActive;
    }

    public Guid Id { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public DemoRole Role { get; private set; }
    public bool IsActive { get; private set; }
}
