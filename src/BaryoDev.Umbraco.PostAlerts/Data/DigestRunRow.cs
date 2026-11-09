using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace BaryoDev.Umbraco.PostAlerts.Data;

public enum DigestRunStatus
{
    /// <summary>Claimed and sending. A run that stays here after a crash is never retried, so a week is sent at most once.</summary>
    Started = 0,
    Sent = 1,
    /// <summary>The period had no new posts or no confirmed subscribers.</summary>
    Skipped = 2,
}

/// <summary>One row per digest period. The unique period end is what stops a second send for the same week.</summary>
[TableName(TableName)]
[PrimaryKey(nameof(Id), AutoIncrement = false)]
[ExplicitColumns]
public sealed class DigestRunRow
{
    public const string TableName = "baryoPostAlertsDigestRun";

    [Column("Id")]
    [PrimaryKeyColumn(AutoIncrement = false)]
    public Guid Id { get; set; }

    [Column("PeriodStartUtc")]
    public DateTime PeriodStartUtc { get; set; }

    [Column("PeriodEndUtc")]
    [Index(IndexTypes.UniqueNonClustered, Name = "IX_baryoPostAlertsDigestRun_PeriodEndUtc")]
    public DateTime PeriodEndUtc { get; set; }

    [Column("Status")]
    public int Status { get; set; }

    [Column("PostCount")]
    public int PostCount { get; set; }

    /// <summary>Confirmed subscribers when the run started.</summary>
    [Column("Recipients")]
    public int Recipients { get; set; }

    /// <summary>Emails the mail server accepted.</summary>
    [Column("Sent")]
    public int Sent { get; set; }

    [Column("StartedUtc")]
    public DateTime StartedUtc { get; set; }

    [Column("CompletedUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? CompletedUtc { get; set; }
}
