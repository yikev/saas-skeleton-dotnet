using Microsoft.EntityFrameworkCore;
using SaaSSkeleton.Contracts;
using SaaSSkeleton.Data;
using SaaSSkeleton.Domain.Auth;
using SaaSSkeleton.Domain.Entities;

var builder = WebApplication.CreateBuilder(args);

var connString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:Default");

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(connString));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHealthChecks()
    .AddNpgSql(connString);

builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Request-Id"] = context.TraceIdentifier;
    await next();
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    app.MapPost("/dev/seed-admin", async (SeedAdminRequest req, AppDbContext db) =>
    {
        var email = req.Email.Trim().ToLowerInvariant();

        var existing = await db.Users
            .Where(u => u.Email == email)
            .Select(u => new { u.Id, u.OrgId, u.Email })
            .FirstOrDefaultAsync();

        if (existing is not null)
        {
            var role = await db.Memberships
                .Where(m => m.UserId == existing.Id && m.OrgId == existing.OrgId)
                .Select(m => m.Role)
                .FirstOrDefaultAsync();

            return Results.Ok(new
            {
                OrgId = existing.OrgId,
                UserId = existing.Id,
                Email = existing.Email,
                Role = role.ToString(),
                AlreadyExisted = true
            });
        }

        var org = new Org
        {
            Id = Guid.NewGuid(),
            Name = req.OrgName.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        var user = new User
        {
            Id = Guid.NewGuid(),
            OrgId = org.Id,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            CreatedAt = DateTimeOffset.UtcNow
        };

        var membership = new Membership
        {
            OrgId = org.Id,
            UserId = user.Id,
            Role = OrgRole.Admin,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.Orgs.Add(org);
        db.Users.Add(user);
        db.Memberships.Add(membership);

        await db.SaveChangesAsync();

        return Results.Created($"/orgs/{org.Id}", new { OrgId = org.Id, UserId = user.Id, Email = user.Email, Role = membership.Role.ToString(), AlreadyExisted = false });
    })
    .WithName("DevSeedAdmin");
}

app.MapHealthChecks("/health");

app.UseHttpsRedirection();

app.MapGet("/weatherforecast", () =>
{
    var summaries = new[]
    {
        "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    };

    return Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast(
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        )).ToArray();
})
.WithName("GetWeatherForecast")
.WithOpenApi();

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}