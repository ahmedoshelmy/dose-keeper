namespace DoseKeeper.Domain;

/// <summary>A medicine the user keeps at home, with a pill count that doses draw down.</summary>
public sealed class Medication
{
    private Medication(string id, string name, string strength, int pillsOnHand, int refillThresholdDays)
    {
        Id = id;
        Name = name;
        Strength = strength;
        PillsOnHand = pillsOnHand;
        RefillThresholdDays = refillThresholdDays;
    }

    public string Id { get; }
    public string Name { get; }
    public string Strength { get; }
    public int PillsOnHand { get; private set; }
    public int RefillThresholdDays { get; }

    public static Medication Create(string id, string name, string strength, int pillsOnHand, int refillThresholdDays)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("A medication needs a name.");
        }

        if (pillsOnHand < 0)
        {
            throw new DomainException("Pills on hand cannot be negative.");
        }

        if (refillThresholdDays is < 0 or > 90)
        {
            throw new DomainException("Refill threshold must be between 0 and 90 days.");
        }

        return new Medication(id, name.Trim(), strength.Trim(), pillsOnHand, refillThresholdDays);
    }

    /// <summary>Takes pills out of stock for one intake; stock never goes below zero.</summary>
    public void Consume(int pills)
    {
        if (pills <= 0)
        {
            throw new DomainException("An intake consumes at least one pill.");
        }

        PillsOnHand = Math.Max(0, PillsOnHand - pills);
    }

    public void Refill(int pills)
    {
        if (pills <= 0)
        {
            throw new DomainException("A refill adds at least one pill.");
        }

        PillsOnHand += pills;
    }
}
