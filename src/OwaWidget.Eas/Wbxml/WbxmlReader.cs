using System.Text;

namespace OwaWidget.Eas.Wbxml;

public static class WbxmlReader
{
    private const byte SwitchPageToken = 0x00;
    private const byte EndToken = 0x01;
    private const byte StrIToken = 0x03;
    private const byte StrTToken = 0x83;
    private const byte OpaqueToken = 0xC3;
    private const byte ContentFlag = 0x40;
    private const byte AttributesFlag = 0x80;
    private const byte TagMask = 0x3F;

    public static WbxmlElement Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
        {
            throw new InvalidDataException("WBXML document is too short.");
        }

        var position = 0;
        SkipHeader(data, ref position);

        byte page = 0;
        var root = ReadElement(data, ref position, ref page);
        if (root is null)
        {
            throw new InvalidDataException("WBXML document contains no root element.");
        }

        return root;
    }

    private static void SkipHeader(ReadOnlySpan<byte> data, ref int position)
    {
        position++;
        ReadMultiByteInt(data, ref position);
        ReadMultiByteInt(data, ref position);
        var stringTableLength = ReadMultiByteInt(data, ref position);
        position += stringTableLength;
    }

    private static WbxmlElement? ReadElement(ReadOnlySpan<byte> data, ref int position, ref byte page)
    {
        while (position < data.Length)
        {
            var token = data[position];

            if (token == SwitchPageToken)
            {
                position++;
                page = data[position++];
                continue;
            }

            if (token == EndToken)
            {
                return null;
            }

            position++;

            if ((token & AttributesFlag) != 0)
            {
                throw new NotSupportedException("WBXML attributes are not used by ActiveSync.");
            }

            var element = new WbxmlElement(page, WbxmlCodePages.GetName(page, (byte)(token & TagMask)));

            if ((token & ContentFlag) != 0)
            {
                ReadContent(data, ref position, ref page, element);
            }

            return element;
        }

        return null;
    }

    private static void ReadContent(ReadOnlySpan<byte> data, ref int position, ref byte page, WbxmlElement element)
    {
        StringBuilder? text = null;

        while (position < data.Length)
        {
            var token = data[position];

            switch (token)
            {
                case SwitchPageToken:
                    position++;
                    page = data[position++];
                    continue;

                case EndToken:
                    position++;
                    if (text is not null)
                    {
                        element.Text = text.ToString();
                    }

                    return;

                case StrIToken:
                    position++;
                    (text ??= new StringBuilder()).Append(ReadNullTerminated(data, ref position));
                    continue;

                case StrTToken:
                    position++;
                    ReadMultiByteInt(data, ref position);
                    continue;

                case OpaqueToken:
                    position++;
                    var length = ReadMultiByteInt(data, ref position);
                    var payload = data.Slice(position, length).ToArray();
                    position += length;
                    element.OpaqueData = payload;
                    (text ??= new StringBuilder()).Append(Encoding.UTF8.GetString(payload));
                    continue;

                default:
                    var child = ReadElement(data, ref position, ref page);
                    if (child is not null)
                    {
                        element.Children.Add(child);
                    }

                    continue;
            }
        }

        if (text is not null)
        {
            element.Text = text.ToString();
        }
    }

    private static string ReadNullTerminated(ReadOnlySpan<byte> data, ref int position)
    {
        var start = position;
        while (position < data.Length && data[position] != 0x00)
        {
            position++;
        }

        var value = Encoding.UTF8.GetString(data.Slice(start, position - start));
        position++;
        return value;
    }

    private static int ReadMultiByteInt(ReadOnlySpan<byte> data, ref int position)
    {
        var result = 0;
        byte current;

        do
        {
            current = data[position++];
            result = (result << 7) | (current & 0x7F);
        }
        while ((current & 0x80) != 0);

        return result;
    }
}