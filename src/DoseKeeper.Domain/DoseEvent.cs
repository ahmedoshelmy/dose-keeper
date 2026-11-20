namespace DoseKeeper.Domain;

/// <summary>What happened to one scheduled dose. Keyed by schedule and due instant, so recording twice is idempotent.</summary>
public sealed record DoseEvent(string ScheduleId, DateTimeOffset DueUtc, DoseStatus Status, DateTimeOffset RecordedUtc)
{
    /// <summary>A taken dose is late when it was recorded after the schedule's late window closed.</summary>
    public bool IsLate(TimeSpan lateWindow) => Status == DoseStatus.Taken && RecordedUtc > DueUtc + lateWindow;

    /// <summary>
    /// Decides whether a new outcome may replace the stored one. A user's explicit choice (taken or skipped) wins over
    /// the automatic "missed", but the automatic outcome never overwrites a user's choice.
    /// </summary>
    public static bool CanReplace(DoseEvent? existing, DoseStatus incoming) => existing is null
        || existing.Status == DoseStatus.Pending
        || (existing.Status == DoseStatus.Missed && incoming is DoseStatus.Taken or DoseStatus.Skipped)
        || (existing.Status is DoseStatus.Taken or DoseStatus.Skipped && incoming is DoseStatus.Taken or DoseStatus.Skipped);
}
