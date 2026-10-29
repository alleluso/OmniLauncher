using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using ZstdSharp;
using ZstdSharp.Unsafe;
using System.Text.RegularExpressions;
using OmniLauncher.Models;
using System.IO;
using System.Threading.Tasks;
using System;
using System.Threading;
using System.Collections.Generic;

namespace OmniLauncher.Services.ProjectLoaders;

public class BlenderProjectLoader : IProjectTypeLoader
{
    public string JsonKey => "blender";
    public string GroupName => "Blender";
    public ProjectType ProjectType => ProjectType.Blender;
    public string ProjectPathLabel => "Project file";
    public string ProjectPathPlaceholder => "Select the .blend file…";
    public string? FolderPickerTitle => null;
    public string? FilePickerTitle => "Select the .blend file";
    public IReadOnlyList<string> ProjectFilePatterns => ["*.blend"];

    public ProjectItem CreateUnavailable(string name, string path) =>
        new BlenderProject { Name = name, ProjectPath = path, BlenderVersion = "Unknown" };

    public async Task<ProjectItem> LoadProjectAsync(string name, string path, bool loadFallbackImage)
    {
        var project = new BlenderProject { Name = name, ProjectPath = path, BlenderVersion = "Unknown" };
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            
            // Decompression and thumbnail conversion never run on the UI thread.
            var info = await Task.Run(() => ReadFileAsync(path, loadFallbackImage, null, timeout.Token)).WaitAsync(timeout.Token);
            project.BlenderVersion = info.Version;
            project.FallbackImagePath = info.PreviewPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or ZstdSharp.ZstdException)
        {
            // Missing or unreadable projects remain available to repair or remove.
        }
        return project;
    }

    public static string InferApplicationVersion(string path)
    {
        var match = Regex.Match(path, @"(?i)blender[\s_-]+(\d+\.\d+(?:\.\d+)?)");
        return match.Success ? match.Groups[1].Value : "";
    }

    // .blend stores major/minor, not the patch release. Do not silently upgrade to another minor version.
    public static bool MatchesVersion(string installed, string required) =>
        Version.TryParse(installed, out var application) && Version.TryParse(required, out var project) &&
        application.Major == project.Major && application.Minor == project.Minor;

    // Read only the header and early preview blocks; never execute file scripts.
    public static bool IsProjectValid(string path)
    {
        if (!path.EndsWith(".blend", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) 
            return false;
        
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            ReadFileAsync(path, false, null, timeout.Token).GetAwaiter().GetResult();
            return true;
        }
        catch (Exception) { return false; }
    }

    private static async Task<(string Version, string PreviewPath)> ReadFileAsync(string path, bool preview, string? cacheDirectory, CancellationToken token)
    {
        var snapshot = new FileInfo(path);
        var stamp = $"{snapshot.Length}|{snapshot.LastWriteTimeUtc.Ticks}";
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            16384, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var magic = new byte[4];
        await file.ReadExactlyAsync(magic, token).ConfigureAwait(false);
        file.Position = 0;
        using Stream data = OpenDataStream(file, magic);
        var header = await ReadHeaderAsync(data, token).ConfigureAwait(false);
        if (!preview) return (header.Version, "");
        // Preview failures must not discard a successfully read version.
        try
        {

            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"blend-preview-v1|{Path.GetFullPath(path)}|{stamp}")));
            var cachePath = Path.Combine(cacheDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmniLauncher", "Thumbnails"), key + ".bmp");
            if (File.Exists(cachePath)) return (header.Version, cachePath);
            var block = new byte[header.BlockSize];
            long skipped = 0;
            for (var count = 0; count < 128; count++)
            {
                await data.ReadExactlyAsync(block, token).ConfigureAwait(false);
                var code = Encoding.ASCII.GetString(block, 0, 4);
                var length = header.LargeBlocks ? BinaryPrimitives.ReadInt64LittleEndian(block.AsSpan(16)) : Int32(block.AsSpan(4), header.BigEndian);
                if (length < 0) break;
                if (code == "TEST")
                {
                    if (length < 8 || length > 8 + 4096L * 4096 * 4) break;
                    var shape = new byte[8];
                    await data.ReadExactlyAsync(shape, token).ConfigureAwait(false);
                    var width = Int32(shape, header.BigEndian);
                    var height = Int32(shape.AsSpan(4), header.BigEndian);
                    if (width <= 0 || height <= 0 || width > 4096 || height > 4096 || length != 8 + (long)width * height * 4) break;
                    var rgba = new byte[checked(width * height * 4)];
                    await data.ReadExactlyAsync(rgba, token).ConfigureAwait(false);
                    Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                    var temporary = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        await File.WriteAllBytesAsync(temporary, EncodeBitmap(width, height, rgba), token).ConfigureAwait(false);
                        File.Move(temporary, cachePath, overwrite: true);
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    return (header.Version, cachePath);
                }
                // Blender places TEST after optional REND blocks, before GLOB. Do not scan scene data.
                if (code != "REND" || (skipped += length) > 8 * 1024 * 1024) break;
                var scratch = new byte[8192];
                while (length > 0)
                {
                    var amount = (int)Math.Min(scratch.Length, length);
                    await data.ReadExactlyAsync(scratch.AsMemory(0, amount), token).ConfigureAwait(false);
                    length -= amount;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or OperationCanceledException or ZstdException)
        {
            // A preview is optional (including a malformed preview or an unwritable cache).
        }
        return (header.Version, "");
    }

    private static Stream OpenDataStream(FileStream file, byte[] magic)
    {
        if (magic[0] == 0x1f && magic[1] == 0x8b) return new GZipStream(file, CompressionMode.Decompress, leaveOpen: true);
        var signature = BinaryPrimitives.ReadUInt32LittleEndian(magic);
        if (signature == 0xFD2FB528 || (signature & 0xFFFFFFF0) == 0x184D2A50)
        {
            var stream = new DecompressionStream(file);
            stream.SetParameter(ZSTD_dParameter.ZSTD_d_windowLogMax, 27);
            return stream;
        }
        return file;
    }

    private static async Task<(string Version, int BlockSize, bool BigEndian, bool LargeBlocks)> ReadHeaderAsync(Stream stream, CancellationToken token)
    {
        var bytes = new byte[17];
        await stream.ReadExactlyAsync(bytes.AsMemory(0, 12), token).ConfigureAwait(false);
        if (Encoding.ASCII.GetString(bytes, 0, 7) != "BLENDER") throw new InvalidDataException("Not a project file.");
        int version;
        if (bytes[7] == '-' || bytes[7] == '_')
        {
            if (bytes[8] != 'v' && bytes[8] != 'V') throw new InvalidDataException("Invalid project header.");
            version = ParseDigits(bytes, 9, 3);
            return ($"{version / 100}.{version % 100}", bytes[7] == '-' ? 24 : 20, bytes[8] == 'V', false);
        }
        await stream.ReadExactlyAsync(bytes.AsMemory(12, 5), token).ConfigureAwait(false);
        if (Encoding.ASCII.GetString(bytes, 7, 6) != "17-01v") throw new InvalidDataException("Unsupported project header.");
        version = ParseDigits(bytes, 13, 4);
        return ($"{version / 100}.{version % 100}", 32, false, true);
    }

    private static int ParseDigits(byte[] data, int start, int count)
    {
        var value = 0;
        for (var i = start; i < start + count; i++)
        {
            if (data[i] < '0' || data[i] > '9') throw new InvalidDataException("Invalid project version.");
            value = value * 10 + data[i] - '0';
        }
        return value;
    }

    private static int Int32(ReadOnlySpan<byte> data, bool bigEndian) => bigEndian
        ? BinaryPrimitives.ReadInt32BigEndian(data) : BinaryPrimitives.ReadInt32LittleEndian(data);

    // Both Blender's RGBA preview and positive-height BMP store the bottom row first.
    // Composite transparency onto the same dark background as the project tile.
    private static byte[] EncodeBitmap(int width, int height, byte[] rgba)
    {
        var stride = (width * 3 + 3) & ~3;
        var bitmap = new byte[54 + stride * height];
        using var writer = new BinaryWriter(new MemoryStream(bitmap));
        writer.Write((byte)'B'); writer.Write((byte)'M'); writer.Write(bitmap.Length);
        writer.Write(0); writer.Write(54); writer.Write(40); writer.Write(width); writer.Write(height);
        writer.Write((short)1); writer.Write((short)24); writer.Write(0); writer.Write(stride * height);
        writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var source = (y * width + x) * 4;
                var target = 54 + y * stride + x * 3;
                var alpha = rgba[source + 3];
                for (var channel = 0; channel < 3; channel++)
                    bitmap[target + channel] = (byte)((rgba[source + 2 - channel] * alpha + 51 * (255 - alpha)) / 255);
            }
        return bitmap;
    }
}


