using Microsoft.Extensions.Options;

namespace Tlp.Host.Services;

/// <summary>Runs the cleanup rules on a timer. See <see cref="CleanupPlanner"/>.</summary>
public sealed class CleanupService(Shelf shelf, NightStore store, IOptions<TlpOptions> options, ILogger<CleanupService> log)
    : BackgroundService
{
    public IReadOnlyList<ShelfEntry> Plan() =>
        CleanupPlanner.Due(
            CleanupPlanner.Describe(shelf.List(), store.Nights, store.Pins, options.Value.RetentionDays),
            DateTimeOffset.UtcNow);

    public IReadOnlyList<ShelfEntry> Run()
    {
        var due = Plan();
        if (!options.Value.CleanupEnabled)
        {
            foreach (var e in due) log.LogInformation("Cleanup (disabled) would delete {Path}", e.Path);
            return [];
        }
        var deleted = new List<ShelfEntry>();
        foreach (var e in due)
        {
            try
            {
                if (shelf.Delete(e.Path))
                {
                    deleted.Add(e);
                    log.LogInformation("Cleanup deleted {Path} ({Size} bytes)", e.Path, e.Size);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.LogWarning(ex, "Cleanup could not delete {Path}", e.Path);
            }
        }
        return deleted;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.CleanupInterval);
        do
        {
            Run();
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
