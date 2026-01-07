using SaaSSkeleton.Domain.Auth;

namespace SaaSSkeleton.Domain.Entities;

public sealed class Membership
{
    public Guid OrgId { get; set; }
    public Org Org { get; set; } = null!;

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public OrgRole Role { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}