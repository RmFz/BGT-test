using System.ComponentModel.DataAnnotations;
using System.Reflection;
using BGT_Test.Models.Dtos;

namespace BGT_Test.Tests.Models;

/// <summary>
/// Attributes on record positional parameters sit on the constructor parameters (MVC reads them there),
/// so values are validated against those attributes directly rather than via Validator.TryValidateObject.
/// </summary>
public class CreateOrderRequestValidationTests
{
    private static bool IsValid<TRecord>(string parameterName, object? value)
    {
        var parameter = typeof(TRecord).GetConstructors().Single()
            .GetParameters().Single(p => p.Name == parameterName);
        var attributes = parameter.GetCustomAttributes<ValidationAttribute>();
        var context = new ValidationContext(new object()) { MemberName = parameterName };

        return Validator.TryValidateValue(value!, context, [], attributes);
    }

    private static List<CreateOrderItemRequest> Items(int count) =>
        Enumerable.Range(1, count).Select(i => new CreateOrderItemRequest(i, 1)).ToList();

    [Theory]
    [InlineData(int.MinValue, false)]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(int.MaxValue, true)]
    public void CustomerId_Boundaries(int customerId, bool expectedValid) =>
        Assert.Equal(expectedValid, IsValid<CreateOrderRequest>(nameof(CreateOrderRequest.CustomerId), customerId));

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void Items_CountBoundaries(int count, bool expectedValid) =>
        Assert.Equal(expectedValid, IsValid<CreateOrderRequest>(nameof(CreateOrderRequest.Items), Items(count)));

    [Fact]
    public void Items_Null_IsInvalid() =>
        Assert.False(IsValid<CreateOrderRequest>(nameof(CreateOrderRequest.Items), null));

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(int.MaxValue, true)]
    [InlineData(-5, false)]
    public void ItemProductId_Boundaries(int productId, bool expectedValid) =>
        Assert.Equal(expectedValid, IsValid<CreateOrderItemRequest>(nameof(CreateOrderItemRequest.ProductId), productId));

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(10_000, true)]
    [InlineData(10_001, false)]
    [InlineData(-1, false)]
    public void ItemQuantity_Boundaries(int quantity, bool expectedValid) =>
        Assert.Equal(expectedValid, IsValid<CreateOrderItemRequest>(nameof(CreateOrderItemRequest.Quantity), quantity));
}
