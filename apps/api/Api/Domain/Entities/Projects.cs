namespace SaaSSkeleton.Domain.Entities;

public sealed class Project
{
    public Guid Id { get; set; }

    public Guid OrgId { get; set; }
    public string Name { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public Org Org { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
    public Guid CreatedByUserId { get; set; }
}