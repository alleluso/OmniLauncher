using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OmniLauncher.Models;

namespace OmniLauncher.Services.ProjectLoaders;

/// <summary>Loads documents for the unified Affinity by Canvan application.</summary>
public sealed class AffinityProjectLoader : IProjectTypeLoader
{
    public string JsonKey => "affinity";
    public string GroupName => "Affinity";
    public ProjectType ProjectType => ProjectType.Affinity;
    public string ProjectPathLabel => "Project file";
    public string ProjectPathPlaceholder => "Select the .af file…";
    public string? FolderPickerTitle => null;
    public string? FilePickerTitle => "Select the .af file";
    public IReadOnlyList<string> ProjectFilePatterns => ["*.af"];

    // The proprietary document contents are left to Affinity to validate on opening.
    public static bool IsProjectValid(string path) =>
        string.Equals(Path.GetExtension(path), ".af", StringComparison.OrdinalIgnoreCase) && File.Exists(path);

    public async Task<ProjectItem> LoadProjectAsync(string name, string path, bool loadFallbackImage)
    {
        var project = new ProjectItem
        {
            Name = name,
            ProjectPath = path,
            ApplicationType = ProjectType.Affinity
        };
        if (loadFallbackImage)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                project.FallbackImagePath = await Task.Run(() => CachePreview(path, null, timeout.Token)).WaitAsync(timeout.Token);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                // Optional previews must not prevent missing/unreadable projects from loading.
            }
        }
        return project;
    }

    internal static string CachePreview(string path, string? cacheDirectory, CancellationToken token)
    {
        var info = new FileInfo(path);
        var length = info.Length;
        var modified = info.LastWriteTimeUtc;
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"affinity-preview-v1|{Path.GetFullPath(path)}|{length}|{modified.Ticks}")));
        var target = Path.Combine(cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmniLauncher", "Thumbnails"), key + ".png");
        token.ThrowIfCancellationRequested();
        if (File.Exists(target)) return target;
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var png = ExtractPreview(file, token);
        if (png is null) return "";
        info.Refresh();
        if (!info.Exists || info.Length != length || info.LastWriteTimeUtc != modified) return "";
        token.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, png);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, target, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return target;
    }

    private static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];
    private const long MaxScanBytes = 256L * 1024 * 1024;
    private const int MaxPngBytes = 16 * 1024 * 1024;

    internal static byte[]? ExtractPreview(Stream file, CancellationToken token)
    {
        // PNG scanning is also used by public Affinity preview extractors:
        // https://github.com/omega-slender/affinity-thumbnail-extractor
        // Prefer the smallest complete PNG, which is typically the thumbnail.
        // This is a heuristic, not a parser of Affinity's document/resource tables.
        var limit = Math.Min(file.Length, MaxScanBytes);
        var buffer = new byte[64 * 1024 + 7];
        byte[]? best = null;
        var carry = 0;
        while (file.Position < limit)
        {
            token.ThrowIfCancellationRequested();
            var start = file.Position - carry;
            var read = file.Read(buffer, carry, (int)Math.Min(64 * 1024, limit - file.Position));
            if (read == 0) break;
            var count = carry + read;
            var resume = file.Position;
            var offset = 0;
            while (offset <= count - 8)
            {
                token.ThrowIfCancellationRequested();
                var found = buffer.AsSpan(offset, count - offset).IndexOf(Signature);
                if (found < 0) break;
                offset += found;
                file.Position = start + offset;
                var png = ReadPng(file, limit, token);
                if (png is not null && (best is null || png.Length < best.Length)) best = png;
                file.Position = resume;
                offset += 8;
            }
            carry = Math.Min(7, count);
            buffer.AsSpan(count - carry, carry).CopyTo(buffer);
        }
        return best;
    }

    private static byte[]? ReadPng(Stream file, long limit, CancellationToken token)
    {
        var start = file.Position;
        using var output = new MemoryStream();
        Span<byte> header = stackalloc byte[8];
        Span<byte> checksum = stackalloc byte[4];
        var scratch = new byte[8192];
        try
        {
            file.ReadExactly(header);
            output.Write(header);
            var hasHeader = false;
            var hasData = false;
            for (var chunks = 0; chunks < 4096; chunks++)
            {
                token.ThrowIfCancellationRequested();
                if (file.Position + 12 > limit) return null;
                file.ReadExactly(header);
                var length = BinaryPrimitives.ReadUInt32BigEndian(header);
                var type = header[4..];
                if (length > MaxPngBytes || file.Position + length + 4 > limit ||
                    file.Position + length + 4 - start > MaxPngBytes) return null;
                if (!hasHeader && (!type.SequenceEqual("IHDR"u8) || length != 13)) return null;
                if (hasHeader && type.SequenceEqual("IHDR"u8)) return null;
                var end = type.SequenceEqual("IEND"u8);
                if (end && (length != 0 || !hasData)) return null;
                if (type.SequenceEqual("IDAT"u8) && length > 0) hasData = true;
                output.Write(header);
                var crc = UpdateCrc(uint.MaxValue, type);
                var remaining = (int)length;
                while (remaining > 0)
                {
                    token.ThrowIfCancellationRequested();
                    var amount = Math.Min(scratch.Length, remaining);
                    file.ReadExactly(scratch.AsSpan(0, amount));
                    if (!hasHeader)
                    {
                        var width = BinaryPrimitives.ReadUInt32BigEndian(scratch);
                        var height = BinaryPrimitives.ReadUInt32BigEndian(scratch.AsSpan(4));
                        if (width == 0 || height == 0 || width > 4096 || height > 4096) return null;
                    }
                    crc = UpdateCrc(crc, scratch.AsSpan(0, amount));
                    output.Write(scratch, 0, amount);
                    remaining -= amount;
                }
                file.ReadExactly(checksum);
                if (BinaryPrimitives.ReadUInt32BigEndian(checksum) != ~crc) return null;
                output.Write(checksum);
                hasHeader = true;
                if (end) return output.ToArray();
            }
        }
        catch (EndOfStreamException) { }
        return null;
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xEDB88320u);
        }
        return crc;
    }
}
