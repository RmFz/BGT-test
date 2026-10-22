using BGT_Test.Models.Dtos;
using BGT_Test.Services;
using Microsoft.AspNetCore.Mvc;

namespace BGT_Test.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;

    public OrdersController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    /// <summary>Creates an order and returns its id.</summary>
    [HttpPost]
    [ProducesResponseType<CreateOrderResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CreateOrderResponse>> CreateAsync(
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _orderService.CreateOrderAsync(request, cancellationToken);

        if (result.Succeeded)
        {
            var orderId = result.OrderId!.Value;
            return Created($"/api/orders/{orderId}", new CreateOrderResponse(orderId));
        }

        var (status, title) = result.Error switch
        {
            CreateOrderError.DuplicateProduct => (StatusCodes.Status400BadRequest, "Invalid order items."),
            CreateOrderError.CustomerNotFound => (StatusCodes.Status404NotFound, "Customer not found."),
            CreateOrderError.ProductNotFound => (StatusCodes.Status404NotFound, "Product not found."),
            CreateOrderError.ProductInactive => (StatusCodes.Status422UnprocessableEntity, "Product is not active."),
            CreateOrderError.InsufficientStock => (StatusCodes.Status409Conflict, "Insufficient stock."),
            _ => (StatusCodes.Status500InternalServerError, "Order could not be created.")
        };

        return Problem(detail: result.Detail, statusCode: status, title: title);
    }
}
