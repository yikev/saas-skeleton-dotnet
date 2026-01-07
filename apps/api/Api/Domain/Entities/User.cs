namespace SaaSSkeleton.Domain.Entities;

public sealed class User
{
    public Guid Id { get; set; }

    public Guid OrgId { get; set; }
    public Org Org { get; set; } = null!;

    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }

    public Membership? Membership { get; set; }
}