using System;
using System.Collections.Generic;

namespace TryOutSpot.Web.Data.Entities;

public partial class Sport
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public string? Category { get; set; }

    public string? AgeGroupDivisions { get; set; }

    public string? CompetitionLevels { get; set; }

    public string? TypicalPositions { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<Opportunity> Opportunities { get; set; } = new List<Opportunity>();

    public virtual ICollection<PlayerSport> PlayerSports { get; set; } = new List<PlayerSport>();

    public virtual ICollection<PlayerListing> PlayerListings { get; set; } = new List<PlayerListing>();

    public virtual ICollection<TeamSport> TeamSports { get; set; } = new List<TeamSport>();
}
