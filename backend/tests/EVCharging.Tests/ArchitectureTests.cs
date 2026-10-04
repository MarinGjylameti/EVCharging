using System.Reflection;
using EVCharging.Application.Charging;
using EVCharging.Domain.Identity;

namespace EVCharging.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void DomainAndApplicationCannotDependOnTransportOrPersistence()
    {
        foreach (
            var assembly in new[] { typeof(Account).Assembly, typeof(IDriverCharging).Assembly }
        )
        {
            var dependencies = assembly.GetReferencedAssemblies().Select(x => x.Name!).ToArray();
            Assert.DoesNotContain(
                dependencies,
                x =>
                    x.StartsWith("Microsoft.EntityFrameworkCore")
                    || x.StartsWith("Npgsql")
                    || x.StartsWith("Microsoft.AspNetCore")
                    || x is "EVCharging.Api" or "EVCharging.Infrastructure"
            );
        }
        Assert.DoesNotContain(
            typeof(Account).Assembly.GetReferencedAssemblies(),
            x => x.Name == "EVCharging.Application"
        );
    }

    [Fact]
    public void PublicApplicationContractsDoNotExposeInfrastructureTypes()
    {
        foreach (var type in typeof(IDriverCharging).Assembly.GetExportedTypes())
        {
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(x => x.PropertyType);
            var methods = type.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
            );
            foreach (
                var exposed in properties
                    .Concat(methods.Select(x => x.ReturnType))
                    .Concat(methods.SelectMany(x => x.GetParameters()).Select(x => x.ParameterType))
            )
                AssertSafe(exposed);
        }
    }

    private static void AssertSafe(Type type)
    {
        Assert.DoesNotContain("EVCharging.Infrastructure", type.FullName ?? "");
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", type.FullName ?? "");
        if (type.HasElementType)
            AssertSafe(type.GetElementType()!);
        foreach (var argument in type.GenericTypeArguments)
            AssertSafe(argument);
    }
}
