using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;

namespace OmniLauncher.Services;

/// <summary>
/// Non-blocking file-system checks. On Windows, touching an offline network or removable drive
/// blocks the calling thread for a long time and cannot be cancelled, so every probe runs on the
/// thread pool and the caller awaits it with a deadline. A volume that misses a deadline is
/// remembered as offline for a short period, so further probes against it complete immediately.
/// Nothing here ever blocks the calling thread.
/// </summary>
public static class PathProbe
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan OfflineRetry = TimeSpan.FromSeconds(10);
    private static readonly ConcurrentDictionary<string, DateTime> OfflineUntil = new(StringComparer.OrdinalIgnoreCase);

    public static Task<bool> ExistsAsync(string? path, TimeSpan? timeout = null) =>
        RunAsync(path, p => File.Exists(p) || Directory.Exists(p), false, timeout);

    public static Task<bool> FileExistsAsync(string? path, TimeSpan? timeout = null) =>
        RunAsync(path, File.Exists, false, timeout);

    /// <summary>True when the path's volume recently failed to answer (cheap, never touches the disk).</summary>
    public static bool IsKnownOffline(string? path)
    {
        var root = RootOf(path);
        return root is not null && OfflineUntil.TryGetValue(root, out var until) && DateTime.UtcNow < until;
    }

    /// <summary>Runs <paramref name="probe"/> on the thread pool; yields <paramref name="fallback"/> if it fails or the volume does not answer in time.</summary>
    public static async Task<T> RunAsync<T>(string? path, Func<string, T> probe, T fallback, TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(path) || IsKnownOffline(path)) return fallback;
        var task = Task.Run(() => probe(path));
        try
        {
            return await task.WaitAsync(timeout ?? DefaultTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            if (RootOf(path) is { } root) OfflineUntil[root] = DateTime.UtcNow + OfflineRetry;
            // The abandoned probe finishes on its own; observe any later fault.
            _ = task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            return fallback;
        }
        catch (Exception) { return fallback; }
    }

    private static string? RootOf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { var root = Path.GetPathRoot(path); return string.IsNullOrEmpty(root) ? null : root; }
        catch (ArgumentException) { return null; }
    }
}
