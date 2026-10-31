using System.Runtime.InteropServices;
using System.Windows;

namespace MokiPad.Services;

internal static class MarkdownClipboard
{
    internal static DataObject Payload(string markdown)
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, markdown, false);
        data.SetData("text/markdown", markdown, false);
        data.SetData("text/x-markdown", markdown, false);
        return data;
    }
    internal static bool Write(string markdown)
    {
        try { Clipboard.SetDataObject(Payload(markdown), true); return true; }
        catch (ExternalException) { return false; }
    }
}
