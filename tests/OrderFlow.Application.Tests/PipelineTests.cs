using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OrderFlow.Application.Abstractions;
using OrderFlow.Application.Behaviors;
using OrderFlow.Application.Orders;
using OrderFlow.Contracts;
using OrderFlow.Domain.Common;

namespace OrderFlow.Application.Tests;

public sealed class PipelineTests
{
    private readonly RecordingUnitOfWork _unitOfWork = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Transaction_CommitsOnSuccess()
    {
        var handler = Transactional(_ => Task.FromResult<Result<string>>("ok"));

        Assert.True((await handler.HandleAsync(new TestCommand(), Ct)).IsSuccess);
        Assert.Equal(["begin", "commit"], _unitOfWork.Calls);
    }

    [Fact]
    public async Task Transaction_RollsBackAnExpectedFailure()
    {
        var handler = Transactional(_ => Task.FromResult<Result<string>>(Error.Validation("x", "x")));

        Assert.True((await handler.HandleAsync(new TestCommand(), Ct)).IsFailure);
        Assert.Equal(["begin", "rollback"], _unitOfWork.Calls);
    }

    [Fact]
    public async Task Transaction_TurnsAConcurrencyConflictInto409()
    {
        var handler = Transactional(_ => throw new ConcurrencyConflictException("stale"));

        var result = await handler.HandleAsync(new TestCommand(), Ct);

        Assert.Equal(ErrorKind.Conflict, result.Error?.Kind);
        Assert.Equal(["begin", "rollback"], _unitOfWork.Calls);
    }

    [Fact]
    public async Task Transaction_RollsBackAndRethrowsUnexpectedExceptions()
    {
        var handler = Transactional(_ => throw new TimeoutException("db down"));

        await Assert.ThrowsAsync<TimeoutException>(() => handler.HandleAsync(new TestCommand(), Ct));
        Assert.Equal(["begin", "rollback"], _unitOfWork.Calls);
    }

    [Fact]
    public async Task Instrumentation_PassesResultsAndExceptionsThrough()
    {
        var ok = new InstrumentedCommandHandler<TestCommand, string>(
            new DelegateHandler(_ => Task.FromResult<Result<string>>("ok")), NullLogger<InstrumentedCommandHandler<TestCommand, string>>.Instance);
        var broken = new InstrumentedCommandHandler<TestCommand, string>(
            new DelegateHandler(_ => throw new InvalidOperationException("bug")), NullLogger<InstrumentedCommandHandler<TestCommand, string>>.Instance);

        Assert.Equal("ok", (await ok.HandleAsync(new TestCommand(), Ct)).Value);
        await Assert.ThrowsAsync<InvalidOperationException>(() => broken.HandleAsync(new TestCommand(), Ct));
    }

    [Fact]
    public void AddApplication_WrapsEveryCommandHandlerInTheDecoratorPipeline()
    {
        var services = new ServiceCollection()
            .AddApplication()
            .AddSingleton<IOrderRepository>(new InMemoryOrderRepository())
            .AddSingleton<IOrderQueries>(new EmptyOrderQueries())
            .AddSingleton<IUnitOfWork>(_unitOfWork)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope();

        Assert.IsType<InstrumentedCommandHandler<PlaceOrderCommand, PlaceOrderResult>>(
            scope.ServiceProvider.GetRequiredService<ICommandHandler<PlaceOrderCommand, PlaceOrderResult>>());
        Assert.IsType<InstrumentedCommandHandler<RecordPaymentCommand, OrderResponse>>(
            scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordPaymentCommand, OrderResponse>>());
        Assert.IsType<PaymentCapturedConsumer>(
            Assert.Single(scope.ServiceProvider.GetServices<IIntegrationEventConsumer<PaymentCapturedV1>>()));
    }

    private TransactionalCommandHandler<TestCommand, string> Transactional(Func<TestCommand, Task<Result<string>>> handle) =>
        new(new DelegateHandler(handle), _unitOfWork);

    internal sealed record TestCommand : ICommand<string>;

    private sealed class DelegateHandler(Func<TestCommand, Task<Result<string>>> handle) : ICommandHandler<TestCommand, string>
    {
        public Task<Result<string>> HandleAsync(TestCommand command, CancellationToken cancellationToken) => handle(command);
    }
}
