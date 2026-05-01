using System;
using System.Collections.Generic;

namespace TryOutSpot.Web.Data.Entities;

public partial class UserPlayerRelationship
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid PlayerId { get; set; }

    public string Relationship { get; set; } = null!;

    public bool CanManage { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Player Player { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
