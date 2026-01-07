using Microsoft.EntityFrameworkCore;

namespace SaaSSkeleton.Data;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
}