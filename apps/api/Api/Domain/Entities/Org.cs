namespace SaaSSkeleton.Domain.Entities;

public sealed class Org
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }

    public List<User> Users { get; set; } = new();
}