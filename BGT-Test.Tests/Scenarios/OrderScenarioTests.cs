using BGT_Test.Controllers;
using BGT_Test.Models.Dtos;
using BGT_Test.Services;
using BGT_Test.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BGT_Test.Tests.Scenarios;

/// <summary>
/// Business scenarios exercised end to end: OrdersController -> OrderService -> database (SQLite in-memory).
/// </summary>
public sealed class OrderScenarioTests : IDisposable
{
    private readonly TestDb _testDb = new();

    public void Dispose() => _testDb.Dispose();

    private async Task<ActionResult<CreateOrderResponse>> PlaceOrderAsync(params (int ProductId, int Quantity)[] items)
    {
        await using var db = _testDb.CreateContext();
        var controller = new OrdersController(new OrderService(db, NullLogger<OrderService>.Instance));
        var request = new CreateOrderRequest(
            TestDb.CustomerId,
            items.Select(i => new CreateOrderItemRequest(i.ProductId, i.Quantity)).ToList());

        return await controller.CreateAsync(request, CancellationToken.None);
    }

    [Fact]
    public async Task Scenario_SuccessfulOrderCreation_Returns201AndRecordsOrderAndReducesStock()
    {
        var response = await PlaceOrderAsync((TestDb.ProductA, 2), (TestDb.ProductB, 4));

        var created = Assert.IsType<CreatedResult>(response.Result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        var body = Assert.IsType<CreateOrderResponse>(created.Value);
        Assert.Equal($"/api/orders/{body.OrderId}", created.Location);

        await using var db = _testDb.CreateContext();
        var order = await db.Orders.Include(o => o.OrderItems).SingleAsync();
        Assert.Equal(body.OrderId, order.OrderId);
        Assert.Equal("Pending", order.OrderStatus);
        Assert.Equal(30.00m, order.TotalAmount);
        Assert.Equal(2, order.OrderItems.Count);
        Assert.Equal(3, _testDb.GetStock(TestDb.ProductA));
        Assert.Equal(96, _testDb.GetStock(TestDb.ProductB));
    }

    [Fact]
    public async Task Scenario_ProductInactive_Returns422AndNothingIsOrdered()
    {
        var response = await PlaceOrderAsync((TestDb.InactiveProduct, 1));

        AssertProblem(response, StatusCodes.Status422UnprocessableEntity, "Product is not active.");
        await AssertNothingChangedAsync();
    }

    [Fact]
    public async Task Scenario_ProductOutOfStock_Returns409AndNothingIsOrdered()
    {
        var response = await PlaceOrderAsync((TestDb.ProductA, 6));

        AssertProblem(response, StatusCodes.Status409Conflict, "Insufficient stock.");
        await AssertNothingChangedAsync();
    }

    [Fact]
    public async Task Scenario_ProductDoesNotExist_Returns404AndNothingIsOrdered()
    {
        var response = await PlaceOrderAsync((999, 1));

        AssertProblem(response, StatusCodes.Status404NotFound, "Product not found.");
        await AssertNothingChangedAsync();
    }

    [Fact]
    public async Task Scenario_OneUnavailableProductInBasket_RejectsWholeOrder()
    {
        var response = await PlaceOrderAsync((TestDb.ProductB, 1), (TestDb.InactiveProduct, 1));

        AssertProblem(response, StatusCodes.Status422UnprocessableEntity, "Product is not active.");
        await AssertNothingChangedAsync();
    }

    private static void AssertProblem(ActionResult<CreateOrderResponse> response, int status, string title)
    {
        var objectResult = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(status, objectResult.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(title, problem.Title);
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
    }

    private async Task AssertNothingChangedAsync()
    {
        Assert.Equal(5, _testDb.GetStock(TestDb.ProductA));
        Assert.Equal(100, _testDb.GetStock(TestDb.ProductB));
        await using var db = _testDb.CreateContext();
        Assert.Empty(db.Orders);
    }
}
