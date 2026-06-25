using System.Text;

namespace Transiever.SieveRuler.Services;

public static class SieveStringEscaper
{
    public static string Escape(string value)
    {
        var sb = new StringBuilder(value.Length);

        foreach (var c in value)
        {
            sb.Append(c switch
            {
                '\\' => @"\\",
                '"' => "\\\"",
                '\r' => @"\r",
                '\n' => @"\n",
                '\t' => @"\t",
                _ => c.ToString()
            });
        }

        return sb.ToString();
    }

    public static string EscapeComment(string value)
    {
        return value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
    }
}
