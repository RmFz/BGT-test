using BGT_Test.Services;

namespace BGT_Test.Tests.Services;

public class CreateOrderResultTests
{
    [Fact]
    public void Success_WithOrderId_IsSucceededWithNoError()
    {
        var result = CreateOrderResult.Success(42);

        Assert.True(result.Succeeded);
        Assert.Equal(42, result.OrderId);
        Assert.Equal(CreateOrderError.None, result.Error);
        Assert.Null(result.Detail);
    }

    [Theory]
    [InlineData(CreateOrderError.DuplicateProduct)]
    [InlineData(CreateOrderError.CustomerNotFound)]
    [InlineData(CreateOrderError.ProductNotFound)]
    [InlineData(CreateOrderError.ProductInactive)]
    [InlineData(CreateOrderError.InsufficientStock)]
    public void Failure_WithError_IsNotSucceededAndCarriesDetail(CreateOrderError error)
    {
        var result = CreateOrderResult.Failure(error, "boom");

        Assert.False(result.Succeeded);
        Assert.Null(result.OrderId);
        Assert.Equal(error, result.Error);
        Assert.Equal("boom", result.Detail);
    }
}
