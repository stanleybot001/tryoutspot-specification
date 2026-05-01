using System;
using System.Collections.Generic;

namespace TryOutSpot.Web.Data.Entities;

public partial class PlayerSport
{
    public Guid Id { get; set; }

    public Guid PlayerId { get; set; }

    public Guid SportId { get; set; }

    public string? PrimaryPosition { get; set; }

    public string? SecondaryPositions { get; set; }

    public string? ExperienceLevel { get; set; }

    public int? YearsPlaying { get; set; }

    public string? SkillLevel { get; set; }

    public string? Availability { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Player Player { get; set; } = null!;

    public virtual Sport Sport { get; set; } = null!;
}
