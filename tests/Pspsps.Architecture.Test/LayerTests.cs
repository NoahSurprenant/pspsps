using System.Reflection;

namespace Pspsps.Architecture.Test;

/// <summary>Clean Architecture: Domain ← Application ← Infrastructure ← Server, never the other way.</summary>
public sealed class LayerTests
{
    private static readonly Assembly Domain = typeof(Pspsps.Domain.DomainException).Assembly;
    private static readonly Assembly Application = typeof(Pspsps.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(Pspsps.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly Server = typeof(Program).Assembly;

    private static IEnumerable<TestCaseData> Layers()
    {
        yield return new TestCaseData(Domain, new[] { Application, Infrastructure, Server }).SetName("Domain uses no other layer");
        yield return new TestCaseData(Application, new[] { Infrastructure, Server }).SetName("Application uses only the Domain");
        yield return new TestCaseData(Infrastructure, new[] { Server }).SetName("Infrastructure doesn't use the Server");
    }

    [TestCaseSource(nameof(Layers))]
    public void LayerDependsOnlyInwards(Assembly layer, Assembly[] outer)
    {
        var references = layer.GetReferencedAssemblies().Select(a => a.Name).ToHashSet();

        Assert.That(outer.Select(a => a.GetName().Name).Where(references.Contains), Is.Empty);
    }

    [Test]
    public void DomainUsesNoFrameworks()
    {
        var references = Domain.GetReferencedAssemblies().Select(a => a.Name!);

        Assert.That(
            references.Where(name => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                || name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
                || name.StartsWith("Orleans", StringComparison.Ordinal)),
            Is.Empty);
    }

    [Test]
    public void DomainModelsHaveNoPublicSetters()
    {
        var publicSetters = Domain.GetExportedTypes()
            .Where(t => t.IsClass && !IsRecord(t))
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(p => p.SetMethod?.IsPublic == true)
            .Select(p => $"{p.DeclaringType!.Name}.{p.Name}");

        Assert.That(publicSetters, Is.Empty);
    }

    // Records are values (init-only, copied with "with"), not aggregates.
    private static bool IsRecord(Type type) => type.GetMethod("<Clone>$") is not null;
}
