using System.Text;

namespace TextPeach.Services;

public enum FileKind { Text, Image, Binary, TooLarge, Error }

/// <summary>Alternative "compiled" presentation of a text file.</summary>
public enum RenderKind { None, Markdown, PlainText }

public sealed record LoadedFile(FileKind Kind, string Text, Encoding Encoding, string EncodingName, long Size, DateTime Modified, string? Error = null);

/// <summary>Reads files for viewing: detects encoding, binaries and oversized files.</summary>
public static class FileLoader
{
    public const long MaxTextBytes = 16 * 1024 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static LoadedFile Load(string path)
    {
        try
        {
            var info = new FileInfo(path);
            long size = info.Length;
            var modified = info.LastWriteTime;
            if (Languages.IsImage(path)) return new(FileKind.Image, "", Utf8NoBom, "", size, modified);
            if (Languages.IsKnownBinary(path)) return new(FileKind.Binary, "", Utf8NoBom, "", size, modified);
            if (size > MaxTextBytes) return new(FileKind.TooLarge, "", Utf8NoBom, "", size, modified);

            var bytes = File.ReadAllBytes(path);
            var (text, enc, name) = Decode(bytes);
            if (text is null) return new(FileKind.Binary, "", Utf8NoBom, "", size, modified);
            return new(FileKind.Text, text, enc, name, size, modified);
        }
        catch (Exception ex)
        {
            return new(FileKind.Error, "", Utf8NoBom, "", 0, default, ex.Message);
        }
    }

    /// <summary>Decodes text; returns a null text when the bytes look binary.</summary>
    public static (string? Text, Encoding Encoding, string Name) Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return (Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), new UTF8Encoding(true), "UTF-8 BOM");
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return (Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), Encoding.Unicode, "UTF-16 LE");
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return (Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2), Encoding.BigEndianUnicode, "UTF-16 BE");

        // NUL bytes in the first 8 KB: binary.
        int probe = Math.Min(bytes.Length, 8192);
        for (int i = 0; i < probe; i++)
            if (bytes[i] == 0) return (null, Utf8NoBom, "");

        try
        {
            return (StrictUtf8.GetString(bytes), Utf8NoBom, "UTF-8");
        }
        catch (DecoderFallbackException)
        {
            return (Encoding.Latin1.GetString(bytes), Encoding.Latin1, "Latin-1");
        }
    }

    /// <summary>Quick check used by the project-wide search and summary: text file, small enough.</summary>
    public static string? TryReadText(string path, long maxBytes = 4 * 1024 * 1024)
    {
        try
        {
            if (Languages.IsImage(path) || Languages.IsKnownBinary(path)) return null;
            var info = new FileInfo(path);
            if (info.Length > maxBytes) return null;
            return Decode(File.ReadAllBytes(path)).Text;
        }
        catch { return null; }
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024):0.##} MB",
    };
}
