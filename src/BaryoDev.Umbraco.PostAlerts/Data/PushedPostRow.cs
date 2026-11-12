using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace BaryoDev.Umbraco.PostAlerts.Data;

/// <summary>Marks a post whose first publish has been handled, so a re-publish never sends a second push.</summary>
[TableName(TableName)]
[PrimaryKey(nameof(ContentKey), AutoIncrement = false)]
[ExplicitColumns]
public sealed class PushedPostRow
{
    public const string TableName = "baryoPostAlertsPushed";

    [Column("ContentKey")]
    [PrimaryKeyColumn(AutoIncrement = false)]
    public Guid ContentKey { get; set; }

    [Column("MarkedUtc")]
    public DateTime MarkedUtc { get; set; }

    /// <summary>See <see cref="PushedStatus"/>. Rows from before this column existed count as sent.</summary>
    [Column("Status")]
    [Constraint(Default = (int)PushedStatus.Sent)]
    public int Status { get; set; } = (int)PushedStatus.Sent;
}

public enum PushedStatus
{
    /// <summary>The first publish was seen and a push is waiting. A restart queues it again.</summary>
    Queued = 0,
    /// <summary>The push went out, or the post was already published when the package was installed.</summary>
    Sent = 1,
}
