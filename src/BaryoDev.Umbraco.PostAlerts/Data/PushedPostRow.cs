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
}
