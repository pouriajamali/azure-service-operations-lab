using Operations;

var checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
async Task Throws<T>(Func<Task> action, string name) where T : Exception
{
    try { await action(); } catch (T) { Check(true, name); return; }
    throw new Exception("Expected " + typeof(T).Name + ": " + name);
}
await Throws<ArgumentOutOfRangeException>(() => Task.FromResult(new BoundedReadService(0)), "invalid concurrency rejected");
var service = new BoundedReadService(1);
Check(await service.ReadAsync(_ => Task.FromResult(42), TimeSpan.FromSeconds(5)) == 42, "result propagated");
await Throws<ArgumentNullException>(() => service.ReadAsync<int>(null!, TimeSpan.FromSeconds(1)), "null operation rejected");
await Throws<ArgumentOutOfRangeException>(() => service.ReadAsync(_ => Task.FromResult(0), TimeSpan.Zero), "invalid budget rejected");
using var canceled = new CancellationTokenSource(); canceled.Cancel();
var calls = 0;
await Throws<OperationCanceledException>(() => service.ReadAsync(_ => { calls++; return Task.FromResult(0); }, TimeSpan.FromSeconds(5), canceled.Token), "already canceled read rejected");
Check(calls == 0, "pre-canceled operation never executed");
await Throws<InvalidOperationException>(() => service.ReadAsync<int>(_ => throw new InvalidOperationException(), TimeSpan.FromSeconds(5)), "fault propagated");
Check(await service.ReadAsync(_ => Task.FromResult(1), TimeSpan.FromSeconds(5)) == 1, "fault releases capacity");
await Throws<OperationCanceledException>(() => service.ReadAsync(async ct => { await Task.Delay(Timeout.Infinite, ct); return 0; }, TimeSpan.FromMilliseconds(20)), "cooperative deadline observed");
Check(await service.ReadAsync(_ => Task.FromResult(2), TimeSpan.FromSeconds(5)) == 2, "cancellation releases capacity");
var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var first = service.ReadAsync(async _ => { entered.SetResult(); await finish.Task; return 1; }, TimeSpan.FromSeconds(10));
await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
using var waiting = new CancellationTokenSource();
var second = service.ReadAsync(_ => { calls++; return Task.FromResult(2); }, TimeSpan.FromSeconds(10), waiting.Token);
waiting.Cancel();
await Throws<OperationCanceledException>(() => second, "queued caller can cancel");
Check(calls == 0, "canceled queued work never starts");
finish.SetResult(); Check(await first == 1, "active operation unaffected by queued cancellation");
var nonCooperativeEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var nonCooperativeEnd = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
using var activeCancel = new CancellationTokenSource();
var active = service.ReadAsync(async _ => { nonCooperativeEntered.SetResult(); await nonCooperativeEnd.Task; return 3; }, TimeSpan.FromSeconds(10), activeCancel.Token);
await nonCooperativeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)); activeCancel.Cancel();
Check(!active.IsCompleted, "non-cooperative work not falsely reported stopped");
using var queueCancel = new CancellationTokenSource();
var blocked = service.ReadAsync(_ => Task.FromResult(4), TimeSpan.FromSeconds(10), queueCancel.Token);
Check(!blocked.IsCompleted, "capacity held until physical work completes");
queueCancel.Cancel(); await Throws<OperationCanceledException>(() => blocked, "blocked operation cancels without acquiring");
nonCooperativeEnd.SetResult(); Check(await active == 3, "non-cooperative completion returned truthfully");
Check(await service.ReadAsync(_ => Task.FromResult(5), TimeSpan.FromSeconds(5)) == 5, "capacity reusable after non-cooperative completion");
Console.WriteLine($"PASS {checks} checks; synthetic local operations only");
