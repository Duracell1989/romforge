using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentResults;
using RomForge.Core.Models;
using RomForge.Core.Services;
using Serilog;

namespace RomForge.Core.IO;

public sealed class LocalDatImporter : IDatImporter
{
    private readonly AppDataService _appData;
    private readonly ILogger _logger;

    public LocalDatImporter(AppDataService appData, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _appData = appData;
        _logger = logger.ForContext<LocalDatImporter>();
    }

    public async Task<Result<string>> ImportAsync(
        string sourceDatPath,
        DatHeader header,
        IProgress<ImportProgress>? progress,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(header);

        var destDatPath = Path.Combine(_appData.DatsPath, Path.GetFileName(sourceDatPath));

        if (!string.Equals(sourceDatPath, destDatPath, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                File.Copy(sourceDatPath, destDatPath, overwrite: true);
                _logger.Information(
                    "Imported DAT {FileName} to managed store",
                    Path.GetFileName(sourceDatPath)
                );
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Result.Fail($"Could not copy DAT file: {ex.Message}");
            }
        }

        try
        {
            await CopyImagesAsync(sourceDatPath, header, progress, ct);
        }
        catch (OperationCanceledException ex)
        {
            _logger.Information(
                ex,
                "Image copy cancelled for {Dat}",
                Path.GetFileName(sourceDatPath)
            );
        }

        return Result.Ok(destDatPath);
    }

    private async Task CopyImagesAsync(
        string sourceDatPath,
        DatHeader header,
        IProgress<ImportProgress>? progress,
        CancellationToken ct
    )
    {
        var sourceImgsBase = FindSourceImgsBase(sourceDatPath);
        if (
            sourceImgsBase is null
            || sourceImgsBase.StartsWith(_appData.ImgsPath, StringComparison.OrdinalIgnoreCase)
        )
            return;

        var folderName = string.IsNullOrEmpty(header.ImFolder) ? header.DatName : header.ImFolder;
        var sourceImgFolder = Path.Combine(sourceImgsBase, folderName);
        if (!Directory.Exists(sourceImgFolder))
            return;

        var files = Directory.GetFiles(sourceImgFolder, "*.png", SearchOption.AllDirectories);
        if (files.Length == 0)
            return;

        _logger.Information("Copying {Count} images for {Folder}", files.Length, folderName);

        var destImgFolder = Path.Combine(_appData.ImgsPath, folderName);

        progress?.Report(
            new ImportProgress(Current: 0, Total: files.Length, CurrentFile: string.Empty)
        );

        for (int i = 0; i < files.Length; i++)
        {
            ct.ThrowIfCancellationRequested();

            var file = files[i];
            var relative = Path.GetRelativePath(sourceImgFolder, file);
            var destFile = Path.Combine(destImgFolder, relative);
            var destDir = Path.GetDirectoryName(destFile);
            if (destDir is not null)
                Directory.CreateDirectory(destDir);

            await Task.Run(() => File.Copy(file, destFile, overwrite: true), ct);

            progress?.Report(
                new ImportProgress(
                    Current: i + 1,
                    Total: files.Length,
                    CurrentFile: Path.GetFileName(file)
                )
            );
        }

        _logger.Information(
            "Image copy complete: {Count} files for {Folder}",
            files.Length,
            folderName
        );
    }

    private static string? FindSourceImgsBase(string datFilePath)
    {
        var datDir = Path.GetDirectoryName(datFilePath) ?? string.Empty;
        var parentDir = Path.GetDirectoryName(datDir) ?? string.Empty;

        var parentImgs = Path.Combine(parentDir, "imgs");
        if (Directory.Exists(parentImgs))
            return parentImgs;

        var sameImgs = Path.Combine(datDir, "imgs");
        return Directory.Exists(sameImgs) ? sameImgs : null;
    }
}
