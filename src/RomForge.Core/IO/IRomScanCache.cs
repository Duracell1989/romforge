using System;

namespace RomForge.Core.IO;

public interface IRomScanCache
{
    uint? GetCrc(string filePath, long fileSize, DateTime lastModified);
    uint? GetTrimmedCrc(string filePath, long fileSize, DateTime lastModified);
    void Set(string filePath, long fileSize, DateTime lastModified, uint crc, uint? trimmedCrc = null);
}
