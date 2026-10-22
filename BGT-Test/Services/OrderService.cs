using BGT_Test.Data;
using BGT_Test.Models;
using BGT_Test.Models.Dtos;
using Microsoft.EntityFrameworkCore;

namespace BGT_Test.Services;

public class OrderService : IOrderService
{
    private const string InitialStatus = "Pending";

    private readonly AppDbContext _db;
    private readonly ILogger<OrderService> _logger;

    public OrderService(AppDbContext db, ILogger<OrderService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<CreateOrderResult> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogInformation(
            "Creating order for customer {CustomerId} with {ItemCount} items",
            request.CustomerId,
            request.Items.Count);

        var productIds = request.Items.Select(i => i.ProductId).ToList();
        if (productIds.Distinct().Count() != productIds.Count)
        {
            return Reject(CreateOrderError.DuplicateProduct, "Each product may appear only once per order.");
        }

        var customerExists = await _db.Customers
            .AnyAsync(c => c.CustomerId == request.CustomerId, cancellationToken);
        if (!customerExists)
        {
            return Reject(CreateOrderError.CustomerNotFound, $"Customer {request.CustomerId} was not found.");
        }

        var products = await _db.Products
            .AsNoTracking()
            .Where(p => productIds.Contains(p.ProductId))
            .ToDictionaryAsync(p => p.ProductId, cancellationToken);

        // Friendly pre-check; the conditional UPDATE below is what actually prevents overselling.
        foreach (var item in request.Items)
        {
            if (!products.TryGetValue(item.ProductId, out var product))
            {
                return Reject(CreateOrderError.ProductNotFound, $"Product {item.ProductId} was not found.");
            }

            if (!product.IsActive)
            {
                return Reject(CreateOrderError.ProductInactive, $"Product {item.ProductId} is not active.");
            }

            if (product.StockQuantity < item.Quantity)
            {
                return Reject(
                    CreateOrderError.InsufficientStock,
                    $"Product {item.ProductId} has {product.StockQuantity} in stock; {item.Quantity} requested.");
            }
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        foreach (var item in request.Items)
        {
            var productId = item.ProductId;
            var quantity = item.Quantity;

            var updated = await _db.Products
                .Where(p => p.ProductId == productId && p.IsActive && p.StockQuantity >= quantity)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(p => p.StockQuantity, p => p.StockQuantity - quantity),
                    cancellationToken);

            if (updated == 0)
            {
                // Stock changed (or product was deactivated) after the pre-check; dispose rolls back.
                return Reject(
                    CreateOrderError.InsufficientStock,
                    $"Product {productId} no longer has {quantity} available.");
            }
        }

        var now = DateTime.UtcNow;
        var order = new Order
        {
            CustomerId = request.CustomerId,
            OrderStatus = InitialStatus,
            CreatedAt = now,
            UpdatedAt = now,
            TotalAmount = request.Items.Sum(i => products[i.ProductId].Price * i.Quantity),
            OrderItems = request.Items
                .Select(i => new OrderItem
                {
                    ProductId = i.ProductId,
                    Quantity = i.Quantity,
                    UnitPrice = products[i.ProductId].Price
                })
                .ToList()
        };

        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Order {OrderId} created for customer {CustomerId}, total {TotalAmount}",
            order.OrderId,
            order.CustomerId,
            order.TotalAmount);

        return CreateOrderResult.Success(order.OrderId);
    }

    private CreateOrderResult Reject(CreateOrderError error, string detail)
    {
        _logger.LogWarning("Order rejected: {Error}. {Detail}", error, detail);
        return CreateOrderResult.Failure(error, detail);
    }
}
