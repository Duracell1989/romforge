using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentResults;
using Serilog;

namespace RomForge.Core.IO;

public sealed class SevenZipCliCompressor : IArchiveCompressor
{
    private readonly string? _binaryPath;
    private readonly ILogger _logger;

    public SevenZipCliCompressor(ILogger logger)
    {
        _logger = logger.ForContext<SevenZipCliCompressor>();
        _binaryPath = FindSevenZipBinary();

        if (_binaryPath is null)
            _logger.Warning("7-Zip binary not found; re-archive operations will be unavailable");
        else
            _logger.Debug("7-Zip binary: {Path}", _binaryPath);
    }

    internal SevenZipCliCompressor(ILogger logger, string? binaryPath)
    {
        _logger = logger.ForContext<SevenZipCliCompressor>();
        _binaryPath = binaryPath;
    }

    public bool IsAvailable => _binaryPath is not null;

    /// <exception cref="InvalidOperationException">The compressor is not available.</exception>
    public async Task<Result> CompressAsync(
        string sourceFile,
        string destArchive,
        long romSize,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default,
        string format = "7z"
    )
    {
        if (_binaryPath is null)
            return Result.Fail(
                "7-Zip binary not found. Install 7-Zip (macOS: brew install 7-zip)."
            );

        var totalRam = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var dictMb = ComputeDictionaryMb(romSize, totalRam);
        _logger.Debug(
            "Compressing {Source} → {Dest} (format {Format}, dict {DictMb} MB)",
            Path.GetFileName(sourceFile),
            Path.GetFileName(destArchive),
            format,
            dictMb
        );

        var psi = new ProcessStartInfo(
            _binaryPath,
            BuildArguments(sourceFile, destArchive, romSize, totalRam, format)
        )
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        var sw = Stopwatch.StartNew();
        using var process = new Process();
        process.StartInfo = psi;
        process.Start();

        var stdoutTask = progress is not null
            ? MonitorProgressAsync(process.StandardOutput, progress, cancellationToken)
            : process.StandardOutput.ReadToEndAsync(cancellationToken);

        Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        sw.Stop();

        if (process.ExitCode != 0)
        {
            var stderr = stderrTask.Result.Trim();
            _logger.Error(
                "Compression failed (exit {Code}, {Elapsed}ms): {Stderr}",
                process.ExitCode,
                sw.ElapsedMilliseconds,
                stderr
            );
            return Result.Fail($"7-Zip exited with code {process.ExitCode}: {stderr}");
        }

        _logger.Debug(
            "Compressed {File} in {Elapsed}ms",
            Path.GetFileName(destArchive),
            sw.ElapsedMilliseconds
        );
        return Result.Ok();
    }

    internal static string BuildArguments(
        string sourceFile,
        string destArchive,
        long romSize,
        long totalRamBytes,
        string format = "7z"
    )
    {
        if (format == "zip")
            return $"a -tzip -mx=9 -y \"{destArchive}\" \"{sourceFile}\"";

        var dictMb = ComputeDictionaryMb(romSize, totalRamBytes);
        return $"a -t7z -m0=LZMA -mx=9 -mmf=bt5 -mmc=1000000000 -md={dictMb}m -mfb=273 -mlc=0 -mlp=2 -mpb=2 -y \"{destArchive}\" \"{sourceFile}\"";
    }

    internal static int ComputeDictionaryMb(long romSize, long totalRamBytes)
    {
        if (romSize <= 0)
            return 1;
        var mb = (romSize + 1024L * 1024 - 1) / (1024L * 1024);
        long pow2 = 1;
        while (pow2 < mb)
            pow2 <<= 1;

        var ramCapMb = totalRamBytes > 0 ? totalRamBytes / (1024L * 1024) / 4 : 3840;
        if (ramCapMb < 1)
            ramCapMb = 1;

        return (int)Math.Min(Math.Min(pow2, 3840L), ramCapMb);
    }

    private static async Task MonitorProgressAsync(
        StreamReader output,
        IProgress<int> progress,
        CancellationToken cancellationToken
    )
    {
        while (await output.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            ReadOnlySpan<char> trimmed = line.AsSpan().Trim();
            if (trimmed.EndsWith("%") && int.TryParse(trimmed[..^1].Trim(), out int pct))
                progress.Report(Math.Clamp(pct, 0, 100));
        }
    }

    private static string? FindSevenZipBinary()
    {
        string[] candidates = OperatingSystem.IsWindows()
            ? ["7z.exe", @"C:\Program Files\7-Zip\7z.exe", @"C:\Program Files (x86)\7-Zip\7z.exe"]
            : ["7zz", "7z", "7za"];

        foreach (var candidate in candidates)
        {
            if (Path.IsPathRooted(candidate))
            {
                if (File.Exists(candidate))
                    return candidate;
                continue;
            }

            var found = FindInPath(candidate);
            if (found is not null)
                return found;
        }

        return null;
    }

    private static string? FindInPath(string name)
    {
        var searchDirs = (
            Environment.GetEnvironmentVariable("PATH") ?? string.Empty
        )
            .Split(Path.PathSeparator)
            .Concat(["/opt/homebrew/bin", "/usr/local/bin"]);

        foreach (var dir in searchDirs)
        {
            if (string.IsNullOrEmpty(dir))
                continue;
            var full = Path.Combine(dir, name);
            if (File.Exists(full))
                return full;
        }

        return null;
    }
}
