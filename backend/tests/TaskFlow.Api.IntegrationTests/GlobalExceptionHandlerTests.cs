using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TaskFlow.Api.ErrorHandling;

namespace TaskFlow.Api.IntegrationTests;

public sealed class GlobalExceptionHandlerTests
{
    private static (GlobalExceptionHandler Handler, IProblemDetailsService Problems) Build()
    {
        var problems = Substitute.For<IProblemDetailsService>();
        problems.TryWriteAsync(Arg.Any<ProblemDetailsContext>()).Returns(true);
        return (new GlobalExceptionHandler(problems, Substitute.For<IStringLocalizer<SharedResource>>(), NullLogger<GlobalExceptionHandler>.Instance), problems);
    }

    [Fact]
    public async Task ACancelledRequestIsAClientCancellationNotA500()
    {
        var (handler, problems) = Build();
        using var aborted = new CancellationTokenSource();
        var context = new DefaultHttpContext { RequestAborted = aborted.Token };
        await aborted.CancelAsync();

        var handled = await handler.TryHandleAsync(context, new OperationCanceledException(), TestContext.Current.CancellationToken);

        handled.ShouldBeTrue();
        context.Response.StatusCode.ShouldBe(GlobalExceptionHandler.ClientClosedRequest);
        await problems.DidNotReceive().TryWriteAsync(Arg.Any<ProblemDetailsContext>());
    }

    [Fact]
    public async Task ACancellationWhileTheClientIsStillThereIsStillAnError()
    {
        var (handler, problems) = Build();
        var context = new DefaultHttpContext();

        await handler.TryHandleAsync(context, new OperationCanceledException(), TestContext.Current.CancellationToken);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status500InternalServerError);
        await problems.Received(1).TryWriteAsync(Arg.Any<ProblemDetailsContext>());
    }
}
