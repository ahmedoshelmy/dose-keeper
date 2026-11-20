using Microsoft.Extensions.DependencyInjection;

namespace DoseKeeper.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddDoseKeeperApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<MedicationService>();
        services.AddScoped<ScheduleService>();
        services.AddScoped<DoseService>();
        services.AddScoped<ReminderService>();
        services.AddScoped<AdherenceService>();
        services.AddScoped<RefillCheckService>();
        services.AddScoped<DemoSeeder>();
        return services;
    }
}
