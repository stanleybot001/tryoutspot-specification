using System;
using System.Collections.Generic;

namespace TryOutSpot.Web.Data.Entities;

public partial class UserTeamRole
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid TeamId { get; set; }

    public string Role { get; set; } = null!;

    public DateTime StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Team Team { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
