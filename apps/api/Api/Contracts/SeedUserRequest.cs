namespace SaaSSkeleton.Contracts;

public sealed record SeedUserRequest(
    string OrgName,
    string Email,
    string Password,
    string Role
);