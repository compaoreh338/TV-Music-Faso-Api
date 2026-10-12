using System.Text;

namespace TVMusicFaso.Core.Services;

public static class ExportText
{
    public static Encoding Utf8Bom { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    /// <summary>
    /// MovieJaySX enregistre ses .mpl en UTF-16 LE, sans BOM.
    /// Un fichier UTF-8 s'ouvre, mais la grille reste vide.
    /// </summary>
    public static Encoding MovieJayMpl { get; } = new UnicodeEncoding(bigEndian: false, byteOrderMark: false);

    public static string Origin(bool isBurkinabe) => isBurkinabe ? "Burkinabe" : "Etranger";

    public static byte[] GetUtf8BomBytes(string content) => Utf8Bom.GetBytes(content);

    public static byte[] GetMovieJayMplBytes(string content) => MovieJayMpl.GetBytes(content);
}
