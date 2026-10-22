using System.Data.Common;
using BGT_Test.Models.Dtos;
using BGT_Test.Services;
using BGT_Test.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace BGT_Test.Tests.Services;

public sealed class OrderServiceTests : IDisposable
{
    private readonly TestDb _testDb = new();

    public void Dispose() => _testDb.Dispose();

    private static CreateOrderRequest Request(int customerId, params (int ProductId, int Quantity)[] items) =>
        new(customerId, items.Select(i => new CreateOrderItemRequest(i.ProductId, i.Quantity)).ToList());

    private OrderService CreateService(params IInterceptor[] interceptors) =>
        new(_testDb.CreateContext(interceptors), NullLogger<OrderService>.Instance);

    [Fact]
    public async Task CreateOrderAsync_NullRequest_ThrowsArgumentNullException()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.CreateOrderAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task CreateOrderAsync_ValidSingleItem_ReturnsSuccessWithOrderId()
    {
        var service = CreateService();

        var result = await service.CreateOrderAsync(
            Request(TestDb.CustomerId, (TestDb.ProductA, 2)), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.OrderId);
        Assert.Equal(CreateOrderError.None, result.Error);
        Assert.Null(result.Detail);
    }

    [Fact]
    public async Task CreateOrderAsync_ValidMultipleItems_PersistsOrderWithTotalAndPriceSnapshot()
    {
        var service = CreateService();

        var result = await service.CreateOrderAsync(
            Request(TestDb.CustomerId, (TestDb.ProductA, 2), (TestDb.ProductB, 4)), CancellationToken.None);

        await using var db = _testDb.CreateContext();
        var order = await db.Orders.Include(o => o.OrderItems).SingleAsync();
        Assert.Equal(result.OrderId, order.OrderId);
        Assert.Equal(TestDb.CustomerId, order.CustomerId);
        Assert.Equal("Pending", order.OrderStatus);
        Assert.Equal(30.00m, order.TotalAmount); // 2 * 10.00 + 4 * 2.50
        Assert.Equal(order.CreatedAt, order.UpdatedAt);
        Assert.Equal(2, order.OrderItems.Count);

        var itemA = order.OrderItems.Single(i => i.ProductId == TestDb.ProductA);
        Assert.Equal(2, itemA.Quantity);
        Assert.Equal(10.00m, itemA.UnitPrice);
        var itemB = order.OrderItems.Single(i => i.ProductId == TestDb.ProductB);
        Assert.Equal(4, itemB.Quantity);
        Assert.Equal(2.50m, itemB.UnitPrice);
    }

    [Fact]
    public async Task CreateOrderAsync_ValidItems_DecrementsStockPerProduct()
    {
        var service = CreateService();

        await service.CreateOrderAsync(
            Request(TestDb.CustomerId, (TestDb.ProductA, 2), (TestDb.ProductB, 4)), CancellationToken.None);

        Assert.Equal(3, _testDb.GetStock(TestDb.ProductA));
        Assert.Equal(96, _testDb.GetStock(TestDb.ProductB));
    }

    [Fact]
    public async Task CreateOrderAsync_QuantityEqualsStock_SucceedsAndEmptiesStock()
    {
        var service = CreateService();

        var result = await service.CreateOrderAsync(
            Request(TestDb.CustomerId, (TestDb.ProductA, 5)), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(0, _testDb.GetStock(TestDb.ProductA));
    }

    [Fact]
    public async Task CreateOrderAsync_DuplicateProduct_ReturnsDuplicateProduct()
    {
        var service = CreateService();

        var result = await service.CreateOrderAsync(
            Request(TestDb.CustomerId, (TestDb.ProductA, 1), (TestDb.ProductA, 1)), CancellationToken.None);

        await AssertRejectedAsync(result, CreateOrderError.DuplicateProduct);
    }

    [Fact]
    public async Task CreateOrderAsync_UnknownCustomer_ReturnsCustomerNotFound()
    {
        var service = CreateService();

        var result = await service.CreateOrderAsync(
            Request(999, (TestDb.ProductA, 1)), CancellationToken.None);

        await AssertRejectedAsync(result, CreateOrderError.CustomerNotFound);
    }

    [Fact]
    public async Task CreateOrderAsync_UnknownProduct_ReturnsProductNotFound()
    {
        var service = CreateService();

        var result = await service.CreateOrderAsync(
            Request(TestDb.CustomerId, (999, 1)), CancellationToken.None);

        await AssertRejectedAsync(result, CreateOrderError.ProductNotFound);
    }

    [Fact]
    public async Task CreateOrderAsync_InactiveProduct_ReturnsProductInactive()
    {
        var service = CreateService();

        var result = await service.CreateOrderAsync(
            Request(TestDb.CustomerId, (TestDb.InactiveProduct, 1)), CancellationToken.None);

        await AssertRejectedAsync(result, CreateOrderError.ProductInactive);
    }

    [Fact]
    public async Task CreateOrderAsync_QuantityExceedsStock_ReturnsInsufficientStock()
    {
        var service = CreateService();

        var result = await service.CreateOrderAsync(
            Request(TestDb.CustomerId, (TestDb.ProductA, 6)), CancellationToken.None);

        await AssertRejectedAsync(result, CreateOrderError.InsufficientStock);
    }

    [Fact]
    public async Task CreateOrderAsync_SecondItemFailsPreCheck_LeavesFirstProductStockUntouched()
    {
        var service = CreateService();

        var result = await service.CreateOrderAsync(
            Request(TestDb.CustomerId, (TestDb.ProductB, 1), (TestDb.ProductA, 6)), CancellationToken.None);

        await AssertRejectedAsync(result, CreateOrderError.InsufficientStock);
    }

    [Fact]
    public async Task CreateOrderAsync_StockDrainedAfterPreCheck_RollsBackEarlierDecrements()
    {
        // Simulates a concurrent order: stock of product B is drained after the pre-check passed,
        // right before the transaction starts. Product A is decremented first, then B's conditional
        // UPDATE matches no rows, so A's decrement must be rolled back.
        var interceptor = new DrainStockInterceptor(_testDb, TestDb.ProductB);
        var service = CreateService(interceptor);

        var result = await service.CreateOrderAsync(
            Request(TestDb.CustomerId, (TestDb.ProductA, 2), (TestDb.ProductB, 4)), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(CreateOrderError.InsufficientStock, result.Error);
        Assert.Equal(5, _testDb.GetStock(TestDb.ProductA));
        Assert.Equal(0, _testDb.GetStock(TestDb.ProductB));
        await using var db = _testDb.CreateContext();
        Assert.Empty(db.Orders);
    }

    [Fact]
    public async Task CreateOrderAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.CreateOrderAsync(Request(TestDb.CustomerId, (TestDb.ProductA, 1)), cts.Token));
    }

    private async Task AssertRejectedAsync(CreateOrderResult result, CreateOrderError expected)
    {
        Assert.False(result.Succeeded);
        Assert.Equal(expected, result.Error);
        Assert.Null(result.OrderId);
        Assert.False(string.IsNullOrWhiteSpace(result.Detail));

        // A rejected order must not change any state.
        Assert.Equal(5, _testDb.GetStock(TestDb.ProductA));
        Assert.Equal(100, _testDb.GetStock(TestDb.ProductB));
        await using var db = _testDb.CreateContext();
        Assert.Empty(db.Orders);
        Assert.Empty(db.OrderItems);
    }

    private sealed class DrainStockInterceptor : DbTransactionInterceptor
    {
        private readonly int _productId;
        private readonly TestDb _db;

        public DrainStockInterceptor(TestDb db, int productId)
        {
            _db = db;
            _productId = productId;
        }

        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection,
            TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            using var context = _db.CreateContext();
            context.Database.ExecuteSqlRaw("UPDATE Products SET StockQuantity = 0 WHERE ProductId = {0}", _productId);
            return base.TransactionStartingAsync(connection, eventData, result, cancellationToken);
        }
    }
}
