namespace DoseKeeper.Domain;

public enum DoseStatus
{
    /// <summary>Not due yet, or due and still inside the late window.</summary>
    Pending,
    Taken,
    Skipped,
    Missed,
}
