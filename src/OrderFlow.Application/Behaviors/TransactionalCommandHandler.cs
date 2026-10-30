using OrderFlow.Application.Abstractions;
using OrderFlow.Domain.Common;

namespace OrderFlow.Application.Behaviors;

/// <summary>
/// Runs a command in one transaction: commit on success, roll back on an expected failure or
/// an exception. Optimistic-concurrency conflicts become a 409-style <see cref="Error"/>.
/// </summary>
public sealed class TransactionalCommandHandler<TCommand, TResponse>(
    ICommandHandler<TCommand, TResponse> inner,
    IUnitOfWork unitOfWork) : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    public async Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken cancellationToken)
    {
        await unitOfWork.BeginAsync(cancellationToken);
        try
        {
            var result = await inner.HandleAsync(command, cancellationToken);
            if (result.IsSuccess)
            {
                await unitOfWork.CommitAsync(cancellationToken);
            }
            else
            {
                await unitOfWork.RollbackAsync(cancellationToken);
            }

            return result;
        }
        catch (ConcurrencyConflictException conflict)
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            return ApplicationErrors.ConcurrencyConflict(conflict.Message);
        }
        catch
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
