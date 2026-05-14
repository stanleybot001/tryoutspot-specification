using TryOutSpot.Web.Data.Entities;

namespace TryOutSpot.Web.Data;

public static class TryOutSpotSportsCatalog
{
    public static readonly Sport[] SeedSports =
    [
        new()
        {
            Id = Guid.Parse("a1111111-1111-1111-1111-111111111111"),
            Name = "Softball",
            Category = "Field",
            IsActive = true,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        },
        new()
        {
            Id = Guid.Parse("a2222222-2222-2222-2222-222222222222"),
            Name = "Baseball",
            Category = "Field",
            IsActive = true,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        },
        new()
        {
            Id = Guid.Parse("a3333333-3333-3333-3333-333333333333"),
            Name = "Soccer",
            Category = "Field",
            IsActive = true,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        },
        new()
        {
            Id = Guid.Parse("a4444444-4444-4444-4444-444444444444"),
            Name = "Basketball",
            Category = "Court",
            IsActive = true,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        },
        new()
        {
            Id = Guid.Parse("a5555555-5555-5555-5555-555555555555"),
            Name = "Volleyball",
            Category = "Court",
            IsActive = true,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        }
    ];
}
