using System;
using System.Collections.Generic;

namespace TryOutSpot.Web.Data.Entities;

public partial class Comment
{
    public Guid Id { get; set; }

    public Guid PostId { get; set; }

    public Guid? ParentCommentId { get; set; }

    public Guid UserId { get; set; }

    public string Content { get; set; } = null!;

    public int LikeCount { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public bool IsActive { get; set; }

    public virtual ICollection<Comment> InverseParentComment { get; set; } = new List<Comment>();

    public virtual Comment? ParentComment { get; set; }

    public virtual Post Post { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
