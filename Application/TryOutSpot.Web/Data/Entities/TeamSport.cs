using System;
using System.Collections.Generic;

namespace TryOutSpot.Web.Data.Entities;

public partial class TeamSport
{
    public Guid Id { get; set; }

    public Guid TeamId { get; set; }

    public Guid SportId { get; set; }

    public string? CompetitionLevel { get; set; }

    public string? AgeGroup { get; set; }

    public string? TravelLevel { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Sport Sport { get; set; } = null!;

    public virtual Team Team { get; set; } = null!;
}
