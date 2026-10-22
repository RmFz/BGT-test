using BGT_Test.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BGT_Test.Tests.Data;

public class InfrastructureExtensionsTests
{
    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();

    [Fact]
    public void AddInfrastructure_MissingConnectionString_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.AddInfrastructure(Configuration()));

        Assert.Contains("DefaultConnection", exception.Message);
    }

    [Fact]
    public void AddInfrastructure_WithConnectionString_RegistersDbContext()
    {
        var services = new ServiceCollection();

        var returned = services.AddInfrastructure(
            Configuration(("ConnectionStrings:DefaultConnection", "Server=localhost;Database=x")));

        Assert.Same(services, returned);
        Assert.Contains(services, d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
    }
}
