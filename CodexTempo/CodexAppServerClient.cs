using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace CodexTempo;

/// <summary>
/// Reads the canonical Codex account bucket through the official local Codex
/// App Server. The child process stays alive between polls so a refresh does
/// not repeatedly pay CLI startup cost.
/// </summary>
public sealed class CodexAppServerClient : IDisposable
{
    public const string SourceName = "Codex App Server";

    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private Process? _process;
    private StreamWriter? _input;
    private Task? _outputLoop;
    private bool _initialized;
    private long _nextRequestId;
    private bool _disposed;

    public async Task<UsageSnapshot?> ReadLatestAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return null;

        try
        {
            await EnsureStartedAsync(cancellationToken);
            var response = await SendRequestCoreAsync(
                "account/rateLimits/read",
                cancellationToken);
            return ParseResponse(response, DateTimeOffset.Now);
        }
        catch (Exception ex) when (ex is IOException
                                   or InvalidOperationException
                                   or JsonException
                                   or TimeoutException
                                   or System.ComponentModel.Win32Exception
                                   or UnauthorizedAccessException
                                   or ObjectDisposedException
                                   or OperationCanceledException)
        {
            StopProcess();
            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
                throw;
            return null;
        }
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (_initialized && _process is { HasExited: false }) return;

        await _startGate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized && _process is { HasExited: false }) return;
            StopProcess();

            var executable = ResolveCodexExecutable();
            if (executable is null)
                throw new InvalidOperationException("Codex CLI was not found.");

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("app-server");
            startInfo.ArgumentList.Add("--listen");
            startInfo.ArgumentList.Add("stdio://");

            _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            if (!_process.Start())
                throw new InvalidOperationException("Codex App Server did not start.");

            _input = _process.StandardInput;
            _outputLoop = ReadOutputLoopAsync(_process);
            _ = DrainErrorsAsync(_process);

            var initialized = await SendRequestCoreAsync(
                "initialize",
                cancellationToken,
                new
                {
                    clientInfo = new
                    {
                        name = "codex_tempo",
                        title = "Codex Tempo",
                        version = "1.0.8"
                    }
                });
            if (!initialized.TryGetProperty("result", out _))
                throw new InvalidOperationException("Codex App Server initialization failed.");

            await SendNotificationAsync("initialized", new { }, cancellationToken);
            _initialized = true;
        }
        finally
        {
            _startGate.Release();
        }
    }

    private async Task<JsonElement> SendRequestCoreAsync(
        string method,
        CancellationToken cancellationToken,
        object? parameters = null)
    {
        var id = Interlocked.Increment(ref _nextRequestId);
        var completion = new TaskCompletionSource<JsonElement>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion))
            throw new InvalidOperationException("Duplicate App Server request id.");

        try
        {
            await WriteMessageAsync(new { method, id, @params = parameters }, cancellationToken);
            var response = await completion.Task.WaitAsync(TimeSpan.FromSeconds(8), cancellationToken);
            if (response.TryGetProperty("error", out var error))
                throw new InvalidOperationException(error.ToString());
            return response;
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private Task SendNotificationAsync(
        string method,
        object parameters,
        CancellationToken cancellationToken) =>
        WriteMessageAsync(new { method, @params = parameters }, cancellationToken);

    private async Task WriteMessageAsync(object message, CancellationToken cancellationToken)
    {
        var input = _input ?? throw new InvalidOperationException("App Server input is unavailable.");
        var json = JsonSerializer.Serialize(message);
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await input.WriteLineAsync(json.AsMemory(), cancellationToken);
            await input.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task ReadOutputLoopAsync(Process process)
    {
        try
        {
            while (!process.HasExited)
            {
                var line = await process.StandardOutput.ReadLineAsync();
                if (line is null) break;

                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    if (root.TryGetProperty("id", out var idElement) &&
                        idElement.TryGetInt64(out var id) &&
                        _pending.TryGetValue(id, out var completion))
                        completion.TrySetResult(root.Clone());
                }
                catch (JsonException)
                {
                    // Ignore non-protocol output and keep the connection alive.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            FailPending(ex);
        }
        finally
        {
            _initialized = false;
            FailPending(new IOException("Codex App Server connection closed."));
        }
    }

    private static async Task DrainErrorsAsync(Process process)
    {
        try
        {
            while (!process.HasExited && await process.StandardError.ReadLineAsync() is not null)
            {
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
        }
    }

    private void FailPending(Exception exception)
    {
        foreach (var completion in _pending.Values)
            completion.TrySetException(exception);
    }

    private static UsageSnapshot? ParseResponse(JsonElement response, DateTimeOffset capturedAt)
    {
        if (!response.TryGetProperty("result", out var result)) return null;

        JsonElement limits;
        if (result.TryGetProperty("rateLimitsByLimitId", out var buckets) &&
            buckets.ValueKind == JsonValueKind.Object &&
            buckets.TryGetProperty("codex", out var canonical))
            limits = canonical;
        else if (result.TryGetProperty("rateLimits", out var primary) &&
                 primary.ValueKind == JsonValueKind.Object &&
                 primary.TryGetProperty("limitId", out var limitId) &&
                 limitId.GetString()?.Equals("codex", StringComparison.OrdinalIgnoreCase) == true)
            limits = primary;
        else
            return null;

        LimitWindow? five = null;
        LimitWindow? week = null;
        foreach (var propertyName in new[] { "primary", "secondary" })
        {
            if (!limits.TryGetProperty(propertyName, out var item) ||
                item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("usedPercent", out var used) ||
                !item.TryGetProperty("windowDurationMins", out var window) ||
                !item.TryGetProperty("resetsAt", out var reset))
                continue;

            var parsed = new LimitWindow(
                used.GetDouble(),
                window.GetInt32(),
                DateTimeOffset.FromUnixTimeSeconds(reset.GetInt64()));
            if (parsed.WindowMinutes is >= 270 and <= 330) five = parsed;
            else if (parsed.WindowMinutes is >= 9000 and <= 11000) week = parsed;
        }

        return five is null && week is null
            ? null
            : new UsageSnapshot(five, week, capturedAt, SourceName);
    }

    private static string? ResolveCodexExecutable()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
            == System.Runtime.InteropServices.Architecture.Arm64
            ? "arm64"
            : "x64";
        var npmBinary = Path.Combine(
            appData,
            "npm",
            "node_modules",
            "@openai",
            "codex",
            "node_modules",
            $"@openai/codex-win32-{architecture}".Replace('/', Path.DirectorySeparatorChar),
            "vendor",
            architecture == "arm64" ? "aarch64-pc-windows-msvc" : "x86_64-pc-windows-msvc",
            "bin",
            "codex.exe");
        if (File.Exists(npmBinary)) return npmBinary;

        try
        {
            foreach (var process in Process.GetProcessesByName("codex"))
            {
                using (process)
                {
                    var mainPath = process.MainModule?.FileName;
                    if (string.IsNullOrWhiteSpace(mainPath)) continue;
                    var candidate = Path.Combine(Path.GetDirectoryName(mainPath)!, "resources", "codex.exe");
                    if (File.Exists(candidate)) return candidate;
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                   or System.ComponentModel.Win32Exception
                                   or UnauthorizedAccessException)
        {
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path)) return null;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), "codex.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
            }
        }
        return null;
    }

    private void StopProcess()
    {
        _initialized = false;
        _input?.Dispose();
        _input = null;

        if (_process is not null)
        {
            try
            {
                if (!_process.HasExited)
                    _process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException
                                       or System.ComponentModel.Win32Exception)
            {
            }
            _process.Dispose();
            _process = null;
        }

        FailPending(new IOException("Codex App Server stopped."));
    }

    public static bool RunSelfTest()
    {
        using var document = JsonDocument.Parse(
            """
            {"id":1,"result":{"rateLimits":{"limitId":"codex","primary":{"usedPercent":99,"windowDurationMins":10080,"resetsAt":1785903281},"secondary":null},"rateLimitsByLimitId":{"codex_bengalfox":{"limitId":"codex_bengalfox","primary":{"usedPercent":0,"windowDurationMins":10080,"resetsAt":1786071255}},"codex":{"limitId":"codex","primary":{"usedPercent":38,"windowDurationMins":10080,"resetsAt":1785903281},"secondary":null}}}}
            """);
        var snapshot = ParseResponse(document.RootElement, DateTimeOffset.UnixEpoch);
        return snapshot?.Week?.UsedPercent == 38 &&
               snapshot.SourceFile == SourceName;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopProcess();
        _startGate.Dispose();
        _writeGate.Dispose();
    }
}
