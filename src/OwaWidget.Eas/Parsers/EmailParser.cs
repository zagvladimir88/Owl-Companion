using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using OwaWidget.Eas.Models;
using OwaWidget.Eas.Wbxml;

namespace OwaWidget.Eas.Parsers;

public static partial class EmailParser
{
    public static EasMessage Parse(string serverId, string collectionId, WbxmlElement data)
    {
        var from = data.ChildText("From");
        var (name, address) = SplitAddress(from);

        return new EasMessage
        {
            ServerId = serverId,
            CollectionId = collectionId,
            From = from,
            FromName = name,
            FromAddress = address,
            Subject = data.ChildText("Subject"),
            DateReceived = ParseDate(data.ChildText("DateReceived")),
            IsRead = data.ChildText("Read") == "1",
            ThreadTopic = data.ChildText("ThreadTopic"),
            ConversationId = data.ChildText("ConversationId"),
            MessageClass = data.ChildText("MessageClass"),
            Importance = data.ChildInt("Importance") ?? 1,
            Preview = ExtractPreview(data),
            Body = ExtractBody(data)
        };
    }

    public static (string? Name, string? Address) SplitAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (null, null);
        }

        var separator = value.IndexOf(',');
        if (separator > 0 && value.IndexOf('<') > separator)
        {
            value = value[..separator];
        }

        var open = value.LastIndexOf('<');
        var close = value.LastIndexOf('>');

        if (open >= 0 && close > open)
        {
            var address = value[(open + 1)..close].Trim();
            var name = value[..open].Trim().Trim('"').Trim();
            return (name.Length > 0 ? name : null, address.Length > 0 ? address : null);
        }

        var trimmed = value.Trim().Trim('"').Trim();
        return (null, trimmed.Length > 0 ? trimmed : null);
    }

    public static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        const DateTimeStyles styles = DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal;

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, styles, out var parsed))
        {
            return parsed;
        }

        if (DateTimeOffset.TryParseExact(value, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture, styles, out parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string? ExtractBody(WbxmlElement data)
    {
        var text = data.Child("Body")?.ChildText("Data");
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var builder = new StringBuilder(text.Length);
        var blankRun = 0;

        foreach (var line in text.Split('\n'))
        {
            var cleaned = LinkNoise().Replace(line, string.Empty);
            cleaned = new string(cleaned.Where(c => !IsInvisible(c)).ToArray()).TrimEnd();

            if (cleaned.Trim().Length == 0)
            {
                if (++blankRun > 1)
                {
                    continue;
                }
            }
            else
            {
                blankRun = 0;
            }

            builder.AppendLine(cleaned);
        }

        return builder.ToString().Trim();
    }

    private static string? ExtractPreview(WbxmlElement data)
    {
        var text = data.Child("Body")?.ChildText("Data") ?? data.ChildText("Preview");
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return Condense(text, 200);
    }

    public static string Condense(string text, int maxLength)
    {
        text = LinkNoise().Replace(text, " ");

        var builder = new StringBuilder(Math.Min(text.Length, maxLength + 1));
        var lastWasSpace = false;

        foreach (var character in text)
        {
            if (IsInvisible(character))
            {
                continue;
            }

            var current = char.IsWhiteSpace(character) ? ' ' : character;

            if (current == ' ')
            {
                if (lastWasSpace || builder.Length == 0)
                {
                    continue;
                }

                lastWasSpace = true;
            }
            else
            {
                lastWasSpace = false;
            }

            builder.Append(current);

            if (builder.Length > maxLength)
            {
                break;
            }
        }

        var result = builder.ToString().TrimEnd();
        return result.Length > maxLength ? result[..maxLength].TrimEnd() + "…" : result;
    }

    private static bool IsInvisible(char character)
    {
        if (character is '͏' or '­' or '﻿')
        {
            return true;
        }

        return CharUnicodeInfo.GetUnicodeCategory(character) is UnicodeCategory.Format;
    }

    [GeneratedRegex(@"\[\s*(?:https?|mailto|cid):[^\]]*\]|<\s*(?:https?|mailto|cid):[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex LinkNoise();
}