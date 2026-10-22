using BGT_Test.Data;
using BGT_Test.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BGT_Test.Tests.Support;

/// <summary>
/// SQLite in-memory database. The EF InMemory provider is not used because OrderService relies on
/// ExecuteUpdateAsync and transactions, which it does not support.
/// </summary>
public sealed class TestDb : IDisposable
{
    public const int CustomerId = 1;
    public const int ProductA = 1;   // price 10.00, stock 5, active
    public const int ProductB = 2;   // price 2.50, stock 100, active
    public const int InactiveProduct = 3;

    private readonly SqliteConnection _connection;

    public TestDb()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var context = CreateContext();
        context.Database.EnsureCreated();

        context.Customers.Add(new Customer
        {
            CustomerId = CustomerId,
            CustomerName = "Test Customer",
            Email = "test@example.com",
            CreatedAt = DateTime.UtcNow
        });
        context.Products.AddRange(
            new Product { ProductId = ProductA, ProductName = "A", Price = 10.00m, StockQuantity = 5, IsActive = true },
            new Product { ProductId = ProductB, ProductName = "B", Price = 2.50m, StockQuantity = 100, IsActive = true },
            new Product { ProductId = InactiveProduct, ProductName = "Inactive", Price = 1.00m, StockQuantity = 10, IsActive = false });
        context.SaveChanges();
    }

    public AppDbContext CreateContext(params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(interceptors)
            .Options;

        return new AppDbContext(options);
    }

    public int GetStock(int productId)
    {
        using var context = CreateContext();
        return context.Products.AsNoTracking().Single(p => p.ProductId == productId).StockQuantity;
    }

    public void Dispose() => _connection.Dispose();
}
