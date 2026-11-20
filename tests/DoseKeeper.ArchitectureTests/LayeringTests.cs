using System.Reflection;
using NetArchTest.Rules;

namespace DoseKeeper.ArchitectureTests;

/// <summary>Clean Architecture dependency rule: Domain &lt;- Application &lt;- Infrastructure &lt;- Functions (composition root).</summary>
public sealed class LayeringTests
{
    private static readonly Assembly Domain = typeof(DoseKeeper.Domain.DoseSchedule).Assembly;
    private static readonly Assembly Application = typeof(DoseKeeper.Application.DoseService).Assembly;
    private static readonly Assembly Infrastructure = typeof(DoseKeeper.Infrastructure.StorageNames).Assembly;

    [Fact]
    public void Domain_is_pure_csharp()
    {
        var result = Types.InAssembly(Domain).ShouldNot()
            .HaveDependencyOnAny("DoseKeeper.Application", "DoseKeeper.Infrastructure", "DoseKeeper.Functions", "Azure", "Microsoft.Azure", "Microsoft.DurableTask", "Microsoft.Extensions")
            .GetResult();

        result.FailingTypeNames.ShouldBeNull();
    }

    [Fact]
    public void Application_knows_no_storage_or_functions_types()
    {
        var result = Types.InAssembly(Application).ShouldNot()
            .HaveDependencyOnAny("DoseKeeper.Infrastructure", "DoseKeeper.Functions", "Azure", "Microsoft.Azure", "Microsoft.DurableTask")
            .GetResult();

        result.FailingTypeNames.ShouldBeNull();
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_the_functions_host()
    {
        var result = Types.InAssembly(Infrastructure).ShouldNot()
            .HaveDependencyOnAny("DoseKeeper.Functions", "Microsoft.Azure.Functions", "Microsoft.DurableTask")
            .GetResult();

        result.FailingTypeNames.ShouldBeNull();
    }

    [Fact]
    public void Repositories_are_sealed_adapters_behind_application_ports()
    {
        var result = Types.InAssembly(Infrastructure).That().HaveNameStartingWith("Table").Should()
            .BeSealed().And().ImplementInterface(typeof(DoseKeeper.Application.IMedicationRepository))
            .Or().ImplementInterface(typeof(DoseKeeper.Application.IScheduleRepository))
            .Or().ImplementInterface(typeof(DoseKeeper.Application.IDoseEventRepository))
            .Or().ImplementInterface(typeof(DoseKeeper.Application.INotificationLog))
            .Or().ImplementInterface(typeof(DoseKeeper.Application.IAdherenceReportStore))
            .GetResult();

        result.FailingTypeNames.ShouldBeNull();
    }
}
