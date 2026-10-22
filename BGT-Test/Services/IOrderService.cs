using BGT_Test.Models.Dtos;

namespace BGT_Test.Services;

public interface IOrderService
{
    /// <summary>
    /// Validates the request against customer/product data, deducts stock and persists the order.
    /// Expected business failures are reported through <see cref="CreateOrderResult.Error"/>.
    /// </summary>
    Task<CreateOrderResult> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken);
}
