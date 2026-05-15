using System;

namespace TryOutSpot.Web.Data.Entities;

public sealed class ZipCodeGeography
{
    public string ZipCode { get; set; } = null!;

    public string? City { get; set; }

    public string? State { get; set; }

    public decimal Latitude { get; set; }

    public decimal Longitude { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
