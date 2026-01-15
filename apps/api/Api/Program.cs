using Microsoft.EntityFrameworkCore;
using SaaSSkeleton.Contracts;
using SaaSSkeleton.Data;
using SaaSSkeleton.Domain.Auth;
using SaaSSkeleton.Domain.Entities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;

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

var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "saas-skeleton";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "saas-skeleton";
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Missing Jwt:Key");

var accessTokenMinutes = int.Parse(builder.Configuration["Jwt:AccessTokenMinutes"] ?? "15");
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = signingKey,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseExceptionHandler();

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Request-Id"] = context.TraceIdentifier;
    await next();
});

app.UseAuthentication();
app.UseAuthorization();

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

var auth = app.MapGroup("/auth");

app.MapHealthChecks("/health");

app.UseHttpsRedirection();

auth.MapGet("/me", (ClaimsPrincipal user) =>
{
    return Results.Ok(new
    {
        UserId = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier),
        OrgId = user.FindFirstValue("org_id"),
        Email = user.FindFirstValue("email") ?? user.FindFirstValue(ClaimTypes.Email),
        Role = user.FindFirstValue("role") ?? user.FindFirstValue(ClaimTypes.Role)
    });
})
.RequireAuthorization();

auth.MapPost("/login", async (LoginRequest req, AppDbContext db, HttpContext context) =>
{
    var email = req.Email.Trim().ToLowerInvariant();

    var user = await db.Users
        .Where(u => u.Email == email)
        .Select(u => new { u.Id, u.OrgId, u.Email, u.PasswordHash })
        .FirstOrDefaultAsync();

    if (user is null)
        return Results.Unauthorized();

    var membership = await db.Memberships
        .Where(m => m.UserId == user.Id && m.OrgId == user.OrgId)
        .Select(m => new { m.Role })
        .FirstOrDefaultAsync();

    if (membership is null)
        return Results.Unauthorized();

    if (!BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
        return Results.Unauthorized();

    // Generate Refresh token and hash it
    string refreshToken = RefreshTokenGenerator.GenerateRefreshToken();
    string refreshTokenHash = RefreshTokenGenerator.HashToken(refreshToken);

    var now = DateTimeOffset.UtcNow;
    var refreshExpiresAt = now.AddDays(14);

    db.RefreshTokens.Add( new RefreshToken
    {
        Id = Guid.NewGuid(),
        UserId = user.Id,
        TokenHash = refreshTokenHash,
        CreatedAt = now,
        ExpiresAt = refreshExpiresAt
    });

    await db.SaveChangesAsync();

    context.Response.Cookies.Append(
    "refresh_token",
    refreshToken,
    new CookieOptions
    {
        HttpOnly = true,
        Secure = false,                // dev only
        SameSite = SameSiteMode.Lax,
        Expires = refreshExpiresAt,
        Path = "/auth"
    });

    var claims = new List<Claim>
    {
        new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
        new("org_id", user.OrgId.ToString()),
        new("role", membership.Role.ToString()),
        new(JwtRegisteredClaimNames.Email, user.Email),
        new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
    };

    var creds = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

    var token = new JwtSecurityToken(
        issuer: jwtIssuer,
        audience: jwtAudience,
        claims: claims,
        notBefore: now.UtcDateTime,
        expires: now.AddMinutes(accessTokenMinutes).UtcDateTime,
        signingCredentials: creds
    );

    var tokenStr = new JwtSecurityTokenHandler().WriteToken(token);

    return Results.Ok(new
    {
        AccessToken = tokenStr,
        TokenType = "Bearer",
        ExpiresInSeconds = (int)TimeSpan.FromMinutes(accessTokenMinutes).TotalSeconds
    });
});

auth.MapPost("/refresh", async (AppDbContext db, HttpContext context) =>
{
    var now = DateTimeOffset.UtcNow;
    var refreshExpiresAt = now.AddDays(14);
    
    var refreshToken = context.Request.Cookies["refresh_token"];
    if (string.IsNullOrWhiteSpace(refreshToken))
        return Results.Unauthorized();

    var hashed = RefreshTokenGenerator.HashToken(refreshToken);

    var refreshTokenEntity = await db.RefreshTokens
        .Where(t => 
            t.TokenHash == hashed && 
            t.RevokedAt == null && 
            t.ExpiresAt > now
        )
        .FirstOrDefaultAsync();

    if (refreshTokenEntity is null)
        return Results.Unauthorized();

    var user = await db.Users
        .Where(u => u.Id == refreshTokenEntity.UserId)
        .Select(u => new { u.Id, u.OrgId, u.Email })
        .FirstOrDefaultAsync();

    if (user is null)
        return Results.Unauthorized();

    var membership = await db.Memberships
        .Where(m => m.UserId == user.Id && m.OrgId == user.OrgId)
        .Select(m => new { m.Role })
        .FirstOrDefaultAsync();

    if (membership is null)
        return Results.Unauthorized();

    refreshTokenEntity.RevokedAt = now;

    var newRefreshToken = RefreshTokenGenerator.GenerateRefreshToken();

    var newHash = RefreshTokenGenerator.HashToken(newRefreshToken);

    db.RefreshTokens.Add( new RefreshToken
    {
        Id = Guid.NewGuid(),
        UserId = refreshTokenEntity.UserId,
        TokenHash = newHash,
        CreatedAt = now,
        ExpiresAt = refreshExpiresAt
    });
    await db.SaveChangesAsync();

    context.Response.Cookies.Append(
    "refresh_token",
    newRefreshToken,
    new CookieOptions
    {
        HttpOnly = true,
        Secure = false,                // dev only
        SameSite = SameSiteMode.Lax,
        Expires = refreshExpiresAt,
        Path = "/auth"
    });

    var claims = new List<Claim>
    {
        new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
        new("org_id", user.OrgId.ToString()),
        new("role", membership.Role.ToString()),
        new(JwtRegisteredClaimNames.Email, user.Email),
        new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
    };

    var creds = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

    var token = new JwtSecurityToken(
        issuer: jwtIssuer,
        audience: jwtAudience,
        claims: claims,
        notBefore: now.UtcDateTime,
        expires: now.AddMinutes(accessTokenMinutes).UtcDateTime,
        signingCredentials: creds
    );

    var tokenStr = new JwtSecurityTokenHandler().WriteToken(token);

    return Results.Ok(new
    {
        AccessToken = tokenStr,
        TokenType = "Bearer",
        ExpiresInSeconds = (int)TimeSpan.FromMinutes(accessTokenMinutes).TotalSeconds
    });
});


auth.MapPost("/logout", async (AppDbContext db, HttpContext context) =>
{
    var now = DateTimeOffset.UtcNow;

    if (context.Request.Cookies["refresh_token"] is null)
        return Results.NoContent();

    var cookieValue = context.Request.Cookies["refresh_token"];

    var hashed = RefreshTokenGenerator.HashToken(cookieValue);

    var token = await db.RefreshTokens
        .Where(t => t.TokenHash == hashed && t.RevokedAt == null)
        .FirstOrDefaultAsync();

    if (token != null)
    {
        token.RevokedAt = now;
        await db.SaveChangesAsync();
    }

    context.Response.Cookies.Append(
    "refresh_token",
    "",
    new CookieOptions
    {
        HttpOnly = true,
        Secure = false,               // dev only
        SameSite = SameSiteMode.Lax,
        Expires = DateTimeOffset.UnixEpoch, // expires in the past
        Path = "/auth"
    });

    return Results.NoContent();
});

app.Run();