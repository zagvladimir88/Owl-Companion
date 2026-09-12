using System.Text;

namespace OwaWidget.App.Views;

public static class Tracking
{
    private const char HairSpace = ' ';

    public static string Wide(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length * 2);

        for (var i = 0; i < text.Length; i++)
        {
            builder.Append(text[i]);

            if (i < text.Length - 1 && text[i] != ' ')
            {
                builder.Append(HairSpace);
            }
        }

        return builder.ToString();
    }
}