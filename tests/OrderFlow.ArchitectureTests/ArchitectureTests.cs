using System.Reflection;
using System.Runtime.CompilerServices;
using OrderFlow.Application.Abstractions;
using OrderFlow.Application.Orders;
using OrderFlow.Contracts;
using OrderFlow.Domain.Common;
using OrderFlow.Domain.Orders;

namespace OrderFlow.ArchitectureTests;

/// <summary>
/// Executable architecture rules. They read compiled assembly metadata, so a violation fails
/// the build pipeline instead of slipping through code review.
/// </summary>
public sealed class ArchitectureTests
{
    private static readonly Assembly Domain = typeof(Order).Assembly;
    private static readonly Assembly Contracts = typeof(IIntegrationEvent).Assembly;
    private static readonly Assembly Application = typeof(PlaceOrderHandler).Assembly;
    private static readonly Assembly Infrastructure = typeof(Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly Api = typeof(Program).Assembly;

    private static readonly string[] Frameworks = ["Microsoft.", "Npgsql", "Dapper", "RabbitMQ", "OpenTelemetry"];

    [Fact]
    public void Domain_DependsOnNothingButTheBaseClassLibrary() =>
        AssertNoReferences(Domain, ["OrderFlow.", .. Frameworks]);

    [Fact]
    public void Contracts_HaveNoDependenciesSoOtherServicesCanShareThem() =>
        AssertNoReferences(Contracts, ["OrderFlow.", .. Frameworks]);

    [Fact]
    public void Application_DoesNotDependOnInfrastructureOrDelivery() =>
        AssertNoReferences(Application, [Infrastructure.GetName().Name!, Api.GetName().Name!, "Microsoft.AspNetCore", "Npgsql", "Dapper", "RabbitMQ", "OpenTelemetry"]);

    [Fact]
    public void Infrastructure_DoesNotDependOnTheApiOrAnObservabilityVendor() =>
        AssertNoReferences(Infrastructure, [Api.GetName().Name!, "OpenTelemetry"]);

    [Fact]
    public void DomainModel_CanOnlyBeChangedThroughBehaviour()
    {
        var violations = Domain.GetExportedTypes()
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(property => property.SetMethod is { IsPublic: true } setter && !IsInitOnly(setter))
            .Select(property => $"{property.DeclaringType?.Name}.{property.Name}");

        Assert.Empty(violations);
    }

    [Fact]
    public void DomainEvents_AreSealedImmutableRecords()
    {
        var events = Domain.GetTypes().Where(type => type.IsClass && typeof(IDomainEvent).IsAssignableFrom(type)).ToList();

        Assert.NotEmpty(events);
        Assert.All(events, type =>
        {
            Assert.True(type.IsSealed, $"{type.Name} must be sealed.");
            Assert.NotNull(type.GetMethod("<Clone>$"));
        });
    }

    [Fact]
    public void Handlers_AreSealed()
    {
        var handlerInterfaces = new[] { typeof(ICommandHandler<,>), typeof(IQueryHandler<,>), typeof(IIntegrationEventConsumer<>) };
        var handlers = Application.GetTypes()
            .Where(type => type.IsClass && type.GetInterfaces().Any(i => i.IsGenericType && handlerInterfaces.Contains(i.GetGenericTypeDefinition())))
            .ToList();

        Assert.NotEmpty(handlers);
        Assert.All(handlers, type => Assert.True(type.IsSealed, $"{type.Name} must be sealed."));
    }

    private static void AssertNoReferences(Assembly assembly, string[] forbiddenPrefixes)
    {
        var offending = assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => forbiddenPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();

        Assert.True(offending.Count == 0, $"{assembly.GetName().Name} must not reference: {string.Join(", ", offending)}");
    }

    private static bool IsInitOnly(MethodInfo setter) =>
        setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit));
}
