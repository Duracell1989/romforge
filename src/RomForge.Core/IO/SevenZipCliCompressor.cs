using System;
using System.Collections.Generic;
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
        ArgumentNullException.ThrowIfNull(logger);
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
        string format = "7z",
        CancellationToken cancellationToken = default
    )
    {
        if (_binaryPath is null)
            return Result.Fail(
                "7-Zip binary not found. Install 7-Zip (macOS: brew install 7-zip)."
            );

        // 7-Zip's "a" command adds to an existing archive rather than replacing it. A stale or
        // partial file at the destination (e.g. left behind when the app was killed mid-compress)
        // would be appended to, silently corrupting the output. Remove it first so we always
        // write a fresh archive.
        if (File.Exists(destArchive))
        {
            try
            {
                File.Delete(destArchive);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Error(ex, "Could not remove existing archive {Dest}", destArchive);
                return Result.Fail(
                    $"Could not replace existing archive {Path.GetFileName(destArchive)}: {ex.Message}"
                );
            }
        }

        var totalRam = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var dictMb = ComputeDictionaryMb(romSize, totalRam);
        _logger.Debug(
            "Compressing {Source} → {Dest} (format {Format}, dict {DictMb} MB)",
            Path.GetFileName(sourceFile),
            Path.GetFileName(destArchive),
            format,
            dictMb
        );

        var psi = new ProcessStartInfo(_binaryPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        // Pass each token through ArgumentList so the OS argument encoder handles escaping.
        // A filename containing a quote or space cannot break out and inject extra switches.
        foreach (string arg in BuildArguments(sourceFile, destArchive, romSize, totalRam, format))
            psi.ArgumentList.Add(arg);

        var sw = Stopwatch.StartNew();
        using var process = new Process();
        process.StartInfo = psi;
        process.Start();

        // Kill the child process on cancellation; otherwise the 7-Zip process is orphaned and
        // keeps writing the archive after the user cancels.
        using CancellationTokenRegistration killRegistration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Process already exited between the HasExited check and Kill — nothing to do.
            }
        });

        var stdoutTask = progress is not null
            ? MonitorProgressAsync(process.StandardOutput, progress, cancellationToken)
            : process.StandardOutput.ReadToEndAsync(cancellationToken);

        Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        sw.Stop();

        if (process.ExitCode != 0)
        {
            var stderr = (await stderrTask.ConfigureAwait(false)).Trim();
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

    internal static IReadOnlyList<string> BuildArguments(
        string sourceFile,
        string destArchive,
        long romSize,
        long totalRamBytes,
        string format = "7z"
    )
    {
        if (format == "zip")
            return ["a", "-tzip", "-mx=9", "-y", destArchive, sourceFile];

        int dictMb = ComputeDictionaryMb(romSize, totalRamBytes);
        // 7-Zip LZMA switch reference: https://7-zip.opensource.jp/chm/cmdline/switches/method.htm
        return
        [
            "a",
            destArchive,
            sourceFile,
            "-t7z", // 7z format
            "-m0=LZMA", // LZMA compression method
            "-mx=9", // Ultra compression level
            "-mmf=bt5", // Match finder bt5 (best ratio)
            "-mmc=1000000000", // Match cycles (maximum passes)
            $"-md={dictMb}m", // Dictionary sized to ROM
            "-mfb=273", // Fast bytes (maximum)
            "-mlc=3", // Literal context bits (default; best for mixed binary content)
            "-mlp=0", // Literal position bits (default)
            "-mpb=2", // Position bits (default)
            "-mhc=on", // Compress archive header
            "-ms=on", // Solid mode
            "-y", // Yes to all prompts
        ];
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
        var searchDirs = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
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
