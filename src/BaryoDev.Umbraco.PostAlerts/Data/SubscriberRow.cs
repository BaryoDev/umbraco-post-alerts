using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace BaryoDev.Umbraco.PostAlerts.Data;

public enum SubscriberStatus
{
    Pending = 0,
    Confirmed = 1,
}

/// <summary>An email subscriber. Only what is needed to send and to prove consent is kept.</summary>
[TableName(TableName)]
[PrimaryKey(nameof(Id), AutoIncrement = false)]
[ExplicitColumns]
public sealed class SubscriberRow
{
    public const string TableName = "baryoPostAlertsSubscriber";
    public const int EmailLength = 200;

    [Column("Id")]
    [PrimaryKeyColumn(AutoIncrement = false)]
    public Guid Id { get; set; }

    /// <summary>Lower case.</summary>
    [Column("Email")]
    [Length(EmailLength)]
    [Index(IndexTypes.UniqueNonClustered, Name = "IX_baryoPostAlertsSubscriber_Email")]
    public string Email { get; set; } = string.Empty;

    [Column("Status")]
    public int Status { get; set; }

    /// <summary>SHA-256 of the confirmation token, as hex. Cleared once the address is confirmed.</summary>
    [Column("TokenHash")]
    [Length(64)]
    [NullSetting(NullSetting = NullSettings.Null)]
    [Index(IndexTypes.NonClustered, Name = "IX_baryoPostAlertsSubscriber_TokenHash")]
    public string? TokenHash { get; set; }

    /// <summary>When the confirmation was last requested.</summary>
    [Column("CreatedUtc")]
    public DateTime CreatedUtc { get; set; }

    [Column("ConfirmedUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? ConfirmedUtc { get; set; }
}
