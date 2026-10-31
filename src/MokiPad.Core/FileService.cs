using System.Text;

namespace MokiPad.Core;

public sealed record LoadedFile(string Text, Encoding Encoding, string NewLine);

public static class FileService
{
    static FileService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static async Task<LoadedFile> ReadAsync(string path, CancellationToken token = default)
    {
        string text;
        Encoding encoding;
        await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                   65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true, 65536, true);
            try
            {
                text = await reader.ReadToEndAsync(token);
                encoding = reader.CurrentEncoding;
            }
            catch (DecoderFallbackException)
            {
                stream.Position = 0;
                encoding = Encoding.GetEncoding("GB18030", EncoderFallback.ExceptionFallback,
                    DecoderFallback.ExceptionFallback);
                using var fallback = new StreamReader(stream, encoding, false, 65536, true);
                text = await fallback.ReadToEndAsync(token);
            }
        }

        var nl = text.Contains("\r\n", StringComparison.Ordinal) ? "CRLF" : text.Contains('\r') ? "CR" : "LF";
        return new LoadedFile(text, encoding, nl);
    }
}
