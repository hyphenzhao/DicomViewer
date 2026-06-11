using System.Diagnostics;
using System.Text;

namespace DicomViewer;

internal sealed class ExternalProcessRunner
{
    public sealed class ProcessResult
    {
        public required int ExitCode { get; init; }
        public required string OutputText { get; init; }
        public required string ErrorText { get; init; }
        public bool Success => ExitCode == 0;
    }

    /// <summary>
    /// Runs an external executable asynchronously, capturing stdout line-by-line for progress reporting.
    /// </summary>
    /// <param name="executable">Full path to the executable (e.g. python.exe).</param>
    /// <param name="arguments">Command-line arguments.</param>
    /// <param name="progress">Receives stdout lines prefixed with PROGRESS:percent:message, decoded to (percent, message). Also receives all other stdout lines for logging.</param>
    /// <param name="cancellationToken">Token to cancel the running process.</param>
    /// <returns>ProcessResult with exit code and full output.</returns>
    public static async Task<ProcessResult> RunAsync(
        string executable,
        string arguments,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = Path.GetDirectoryName(executable) ?? string.Empty
            },
            EnableRaisingEvents = true
        };

        // Tell Python to use UTF-8 for stdout/stderr when piped
        process.StartInfo.Environment["PYTHONIOENCODING"] = "utf-8";

        // Limit CPU thread count to prevent contention with multiprocessing
        process.StartInfo.Environment["OMP_NUM_THREADS"] = "2";
        process.StartInfo.Environment["MKL_NUM_THREADS"] = "2";
        process.StartInfo.Environment["OPENBLAS_NUM_THREADS"] = "2";
        process.StartInfo.Environment["NUMEXPR_NUM_THREADS"] = "2";

        var outputLines = new List<string>();
        var errorLines = new List<string>();

        var outputTcs = new TaskCompletionSource<bool>();
        var errorTcs = new TaskCompletionSource<bool>();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                outputTcs.TrySetResult(true);
                return;
            }

            string line = e.Data;
            lock (outputLines)
            {
                outputLines.Add(line);
            }

            progress?.Report(line);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                errorTcs.TrySetResult(true);
                return;
            }

            lock (errorLines)
            {
                errorLines.Add(e.Data);
            }
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using (cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Process may have already exited.
                }
            }))
            {
                await process.WaitForExitAsync(cancellationToken);
            }

            // Give async readers time to flush remaining output.
            await Task.WhenAny(Task.WhenAll(outputTcs.Task, errorTcs.Task), Task.Delay(5000));
        }
        catch (OperationCanceledException)
        {
            progress?.Report("PROGRESS:-1:操作已取消");
            return new ProcessResult
            {
                ExitCode = -1,
                OutputText = string.Join(Environment.NewLine, outputLines),
                ErrorText = string.Join(Environment.NewLine, errorLines)
            };
        }

        return new ProcessResult
        {
            ExitCode = process.ExitCode,
            OutputText = string.Join(Environment.NewLine, outputLines),
            ErrorText = string.Join(Environment.NewLine, errorLines)
        };
    }

    /// <summary>
    /// Parses stdout lines to extract progress reports in the format: PROGRESS:percent:message
    /// </summary>
    public static (int Percent, string Message)? ParseProgressLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("PROGRESS:", StringComparison.Ordinal))
        {
            return null;
        }

        ReadOnlySpan<char> span = line.AsSpan("PROGRESS:".Length);
        int colonIndex = span.IndexOf(':');
        if (colonIndex < 0)
        {
            return null;
        }

        if (!int.TryParse(span[..colonIndex], out int percent))
        {
            return null;
        }

        string message = span[(colonIndex + 1)..].ToString();
        return (Math.Clamp(percent, -1, 100), message);
    }
}
