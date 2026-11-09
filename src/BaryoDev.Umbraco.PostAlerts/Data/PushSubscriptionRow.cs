using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace BaryoDev.Umbraco.PostAlerts.Data;

[TableName(TableName)]
[PrimaryKey(nameof(Id), AutoIncrement = false)]
[ExplicitColumns]
public sealed class PushSubscriptionRow
{
    public const string TableName = "baryoPostAlertsPush";
    public const int EndpointLength = 1000;

    [Column("Id")]
    [PrimaryKeyColumn(AutoIncrement = false)]
    public Guid Id { get; set; }

    /// <summary>SHA-256 of the endpoint, as hex. The unique key, since endpoints are too long to index everywhere.</summary>
    [Column("EndpointHash")]
    [Length(64)]
    [Index(IndexTypes.UniqueNonClustered, Name = "IX_baryoPostAlertsPush_EndpointHash")]
    public string EndpointHash { get; set; } = string.Empty;

    [Column("Endpoint")]
    [Length(EndpointLength)]
    public string Endpoint { get; set; } = string.Empty;

    [Column("P256dh")]
    [Length(128)]
    public string P256dh { get; set; } = string.Empty;

    [Column("Auth")]
    [Length(64)]
    public string Auth { get; set; } = string.Empty;

    [Column("CreatedUtc")]
    public DateTime CreatedUtc { get; set; }
}
