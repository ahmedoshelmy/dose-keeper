using DoseKeeper.Domain;

namespace DoseKeeper.Application;

public sealed class MedicationService(IMedicationRepository medications, IScheduleRepository schedules, TimeProvider clock)
{
    public async Task<Medication> CreateAsync(NewMedication request, CancellationToken ct)
    {
        var medication = Medication.Create(Ids.New("med"), request.Name, request.Strength, request.PillsOnHand, request.RefillThresholdDays);
        await medications.SaveAsync(medication, ct);
        return medication;
    }

    public Task<IReadOnlyList<Medication>> ListAsync(CancellationToken ct) => medications.ListAsync(ct);

    public async Task<Medication> RefillAsync(string medicationId, int pills, CancellationToken ct)
    {
        var medication = await medications.GetAsync(medicationId, ct) ?? throw new NotFoundException($"Medication {medicationId} not found.");
        medication.Refill(pills);
        await medications.SaveAsync(medication, ct);
        return medication;
    }

    public async Task<IReadOnlyList<RefillForecast>> ForecastAsync(CancellationToken ct)
    {
        var allSchedules = await schedules.ListAsync(ct);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var forecasts = (await medications.ListAsync(ct)).Select(m => RefillForecast.For(m, allSchedules, today)).ToList();
        return forecasts;
    }
}
