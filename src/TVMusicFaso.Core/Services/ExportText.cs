using System.Text;

namespace TVMusicFaso.Core.Services;

public static class ExportText
{
    public static Encoding Utf8Bom { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    public static string Origin(bool isBurkinabe) => isBurkinabe ? "Burkinabe" : "Etranger";

    public static byte[] GetUtf8BomBytes(string content) => Utf8Bom.GetBytes(content);
}
