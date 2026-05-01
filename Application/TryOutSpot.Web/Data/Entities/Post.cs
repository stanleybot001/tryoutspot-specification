using System;
using System.Collections.Generic;

namespace TryOutSpot.Web.Data.Entities;

public partial class Post
{
    public Guid Id { get; set; }

    public Guid OpportunityId { get; set; }

    public Guid UserId { get; set; }

    public string Content { get; set; } = null!;

    public string PostType { get; set; } = null!;

    public bool IsPinned { get; set; }

    public int LikeCount { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public bool IsActive { get; set; }

    public virtual ICollection<Comment> Comments { get; set; } = new List<Comment>();

    public virtual Opportunity Opportunity { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
