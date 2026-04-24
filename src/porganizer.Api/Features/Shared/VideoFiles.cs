using System.Buffers.Binary;

namespace porganizer.Api.Features.Shared;

/// <summary>
/// Computes the OpenSubtitles hash (OSHash) for a file.
/// Algorithm: file_size + sum-of-uint64-chunks(first 64 KB) + sum-of-uint64-chunks(last 64 KB),
/// expressed as a 16-character lowercase hex string.
/// </summary>
public static class OsHash
{
    private const int BlockSize = 65536; // 64 KB

    /// <summary>
    /// Returns the OSHash for <paramref name="filePath"/>, or <c>null</c> if the file does not
    /// exist or is smaller than 128 KB (the minimum required by the algorithm).
    /// </summary>
    public static string? Compute(string filePath)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists || info.Length < BlockSize * 2)
            return null;

        ulong hash = (ulong)info.Length;
        var buffer = new byte[BlockSize];

        using var stream = File.OpenRead(filePath);

        stream.ReadExactly(buffer);
        Accumulate(ref hash, buffer);

        stream.Seek(-BlockSize, SeekOrigin.End);
        stream.ReadExactly(buffer);
        Accumulate(ref hash, buffer);

        return hash.ToString("x016");
    }

    private static void Accumulate(ref ulong hash, byte[] buffer)
    {
        for (var i = 0; i < buffer.Length; i += 8)
            hash += BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(i, 8));
    }
}

public static class VideoExtensions
{
    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".m4v",
        ".mpg", ".mpeg", ".ts", ".flv", ".webm", ".vob", ".m2ts"
    };
}
