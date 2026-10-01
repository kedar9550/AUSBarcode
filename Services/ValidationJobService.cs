using System.Collections.Concurrent;
using AUSBarcodeValidator.Models;

namespace AUSBarcodeValidator.Services;

public sealed class ValidationJobService
{
    private readonly ConcurrentDictionary<Guid, (ValidationJob Job, CancellationTokenSource Cts)> _jobs = new();

    public ValidationJob Create()
    {
        var job = new ValidationJob();
        _jobs[job.Id] = (job, new CancellationTokenSource());
        return job;
    }

    public ValidationJob? Get(Guid id) => _jobs.TryGetValue(id, out var item) ? item.Job : null;

    public CancellationToken Token(Guid id) => _jobs[id].Cts.Token;

    public void Cancel(Guid id)
    {
        if (_jobs.TryGetValue(id, out var item))
        {
            item.Job.CancelRequested = true;
            item.Job.Status = "Cancelling... ❌";
            item.Cts.Cancel();
        }
    }

    public void TogglePause(Guid id)
    {
        if (_jobs.TryGetValue(id, out var item))
        {
            item.Job.PauseRequested = !item.Job.PauseRequested;
            item.Job.Status = item.Job.PauseRequested ? "Paused ⏸" : "Resumed ▶";
        }
    }
}
