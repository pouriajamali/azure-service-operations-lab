namespace Operations;

/// <summary>A cooperative timeout. A non-cooperative operation retains its slot until it ends.</summary>
public sealed class BoundedReadService
{
    private readonly SemaphoreSlim gate;

    public BoundedReadService(int concurrency)
    {
        if (concurrency < 1) throw new ArgumentOutOfRangeException(nameof(concurrency));
        gate = new SemaphoreSlim(concurrency, concurrency);
    }

    public async Task<T> ReadAsync<T>(Func<CancellationToken, Task<T>> operation,
        TimeSpan budget, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (budget <= TimeSpan.Zero || budget.TotalMilliseconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(budget));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(budget);
        await gate.WaitAsync(deadline.Token).ConfigureAwait(false);
        try
        {
            deadline.Token.ThrowIfCancellationRequested();
            return await operation(deadline.Token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
}
