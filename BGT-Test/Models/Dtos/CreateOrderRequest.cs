using System.ComponentModel.DataAnnotations;

namespace BGT_Test.Models.Dtos;

public record CreateOrderRequest(
    [Range(1, int.MaxValue)] int CustomerId,
    [Required, MinLength(1), MaxLength(100)] IReadOnlyList<CreateOrderItemRequest> Items);

public record CreateOrderItemRequest(
    [Range(1, int.MaxValue)] int ProductId,
    [Range(1, 10_000)] int Quantity);
