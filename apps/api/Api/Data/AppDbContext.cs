using Microsoft.EntityFrameworkCore;
using SaaSSkeleton.Domain.Entities;
using SaaSSkeleton.Domain.Auth;

namespace SaaSSkeleton.Data;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Org> Orgs => Set<Org>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Membership> Memberships => Set<Membership>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Org>(b =>
        {
            b.ToTable("orgs");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.CreatedAt).IsRequired();

            b.HasMany(x => x.Users)
             .WithOne(x => x.Org)
             .HasForeignKey(x => x.OrgId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<User>(b =>
        {
            b.ToTable("users");
            b.HasKey(x => x.Id);

            b.Property(x => x.Email).HasMaxLength(320).IsRequired();
            b.HasIndex(x => new { x.OrgId, x.Email }).IsUnique(); // email unique per org

            b.Property(x => x.PasswordHash).IsRequired();
            b.Property(x => x.CreatedAt).IsRequired();
        });

        modelBuilder.Entity<Membership>(b =>
        {
            b.ToTable("memberships");
            b.HasKey(x => new { x.OrgId, x.UserId }); // composite PK

            b.Property(x => x.Role)
             .HasConversion<string>()  // store "Admin"/"Member"/"Viewer"
             .HasMaxLength(20)
             .IsRequired();

            b.Property(x => x.CreatedAt).IsRequired();

            b.HasOne(x => x.Org)
             .WithMany()
             .HasForeignKey(x => x.OrgId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(x => x.User)
             .WithOne(x => x.Membership)
             .HasForeignKey<Membership>(x => x.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });
    }
}