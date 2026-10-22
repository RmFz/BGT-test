using System.Text.Json;
using BGT_Test.Middleware;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace BGT_Test.Tests.Middleware;

public class GlobalExceptionHandlerTests
{
    private readonly Mock<ILogger<GlobalExceptionHandler>> _logger = new();

    private GlobalExceptionHandler CreateHandler() => new(_logger.Object);

    private static DefaultHttpContext CreateContext(CancellationToken requestAborted = default)
    {
        var context = new DefaultHttpContext { RequestAborted = requestAborted };
        context.Response.Body = new MemoryStream();
        return context;
    }

    [Fact]
    public async Task TryHandleAsync_UnhandledException_WritesProblemDetailsWithoutLeakingMessage()
    {
        var context = CreateContext();

        var handled = await CreateHandler().TryHandleAsync(
            context, new InvalidOperationException("secret internals"), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.DoesNotContain("secret internals", body);
        var problem = JsonSerializer.Deserialize<ProblemDetails>(body);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status500InternalServerError, problem.Status);
        Assert.Equal("An unexpected error occurred.", problem.Title);
    }

    [Fact]
    public async Task TryHandleAsync_UnhandledException_LogsError()
    {
        var exception = new InvalidOperationException("boom");

        await CreateHandler().TryHandleAsync(CreateContext(), exception, CancellationToken.None);

        _logger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task TryHandleAsync_ClientDisconnected_HandledQuietly()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var context = CreateContext(cts.Token);
        context.Response.StatusCode = StatusCodes.Status200OK;

        var handled = await CreateHandler().TryHandleAsync(
            context, new OperationCanceledException(), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length);
        _logger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task TryHandleAsync_CancellationWithoutClientAbort_TreatedAsServerError()
    {
        var context = CreateContext();

        var handled = await CreateHandler().TryHandleAsync(
            context, new OperationCanceledException(), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
    }
}
