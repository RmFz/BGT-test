namespace BGT_Test.Services;

public enum CreateOrderError
{
    None,
    DuplicateProduct,
    CustomerNotFound,
    ProductNotFound,
    ProductInactive,
    InsufficientStock
}

public record CreateOrderResult(long? OrderId, CreateOrderError Error, string? Detail)
{
    public bool Succeeded => Error == CreateOrderError.None;

    public static CreateOrderResult Success(long orderId) => new(orderId, CreateOrderError.None, null);

    public static CreateOrderResult Failure(CreateOrderError error, string detail) => new(null, error, detail);
}
