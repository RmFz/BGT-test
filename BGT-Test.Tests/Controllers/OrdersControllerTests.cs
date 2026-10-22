using BGT_Test.Controllers;
using BGT_Test.Models.Dtos;
using BGT_Test.Services;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace BGT_Test.Tests.Controllers;

public class OrdersControllerTests
{
    private static readonly CreateOrderRequest Request = new(1, [new CreateOrderItemRequest(1, 2)]);

    private readonly Mock<IOrderService> _orderService = new();

    private OrdersController CreateController() => new(_orderService.Object);

    [Fact]
    public async Task CreateAsync_ServiceSucceeds_ReturnsCreatedWithLocationAndBody()
    {
        _orderService
            .Setup(s => s.CreateOrderAsync(Request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateOrderResult.Success(123));

        var actionResult = await CreateController().CreateAsync(Request, CancellationToken.None);

        var created = Assert.IsType<CreatedResult>(actionResult.Result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Equal("/api/orders/123", created.Location);
        var body = Assert.IsType<CreateOrderResponse>(created.Value);
        Assert.Equal(123, body.OrderId);
    }

    [Theory]
    [InlineData(CreateOrderError.DuplicateProduct, StatusCodes.Status400BadRequest, "Invalid order items.")]
    [InlineData(CreateOrderError.CustomerNotFound, StatusCodes.Status404NotFound, "Customer not found.")]
    [InlineData(CreateOrderError.ProductNotFound, StatusCodes.Status404NotFound, "Product not found.")]
    [InlineData(CreateOrderError.ProductInactive, StatusCodes.Status422UnprocessableEntity, "Product is not active.")]
    [InlineData(CreateOrderError.InsufficientStock, StatusCodes.Status409Conflict, "Insufficient stock.")]
    public async Task CreateAsync_ServiceRejects_ReturnsMappedProblemDetails(
        CreateOrderError error, int expectedStatus, string expectedTitle)
    {
        _orderService
            .Setup(s => s.CreateOrderAsync(Request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateOrderResult.Failure(error, "the detail"));

        var actionResult = await CreateController().CreateAsync(Request, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(actionResult.Result);
        Assert.Equal(expectedStatus, objectResult.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(expectedStatus, problem.Status);
        Assert.Equal(expectedTitle, problem.Title);
        Assert.Equal("the detail", problem.Detail);
    }

    [Fact]
    public async Task CreateAsync_UnmappedError_ReturnsInternalServerError()
    {
        _orderService
            .Setup(s => s.CreateOrderAsync(Request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateOrderResult.Failure((CreateOrderError)999, "unknown"));

        var actionResult = await CreateController().CreateAsync(Request, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(actionResult.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
    }

    [Fact]
    public async Task CreateAsync_Always_ForwardsRequestAndCancellationTokenToService()
    {
        using var cts = new CancellationTokenSource();
        _orderService
            .Setup(s => s.CreateOrderAsync(Request, cts.Token))
            .ReturnsAsync(CreateOrderResult.Success(1));

        await CreateController().CreateAsync(Request, cts.Token);

        _orderService.Verify(s => s.CreateOrderAsync(Request, cts.Token), Times.Once);
    }
}
