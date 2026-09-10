using System.Globalization;
using System.Text;

namespace OwaWidget.Eas.Wbxml;

public sealed class WbxmlWriter
{
    private const byte SwitchPageToken = 0x00;
    private const byte EndToken = 0x01;
    private const byte StrIToken = 0x03;
    private const byte OpaqueToken = 0xC3;
    private const byte ContentFlag = 0x40;

    private readonly MemoryStream _stream = new();
    private byte _currentPage = 0xFF;
    private int _depth;

    public WbxmlWriter()
    {
        _stream.WriteByte(0x03);
        _stream.WriteByte(0x01);
        _stream.WriteByte(0x6A);
        _stream.WriteByte(0x00);
    }

    public WbxmlWriter Start(byte page, string name)
    {
        WriteTag(page, name, hasContent: true);
        _depth++;
        return this;
    }

    public WbxmlWriter EndElement()
    {
        if (_depth == 0)
        {
            throw new InvalidOperationException("No open element to close.");
        }

        _stream.WriteByte(EndToken);
        _depth--;
        return this;
    }

    public WbxmlWriter Empty(byte page, string name)
    {
        WriteTag(page, name, hasContent: false);
        return this;
    }

    public WbxmlWriter Element(byte page, string name, string value)
    {
        WriteTag(page, name, hasContent: true);
        WriteText(value);
        _stream.WriteByte(EndToken);
        return this;
    }

    public WbxmlWriter Element(byte page, string name, int value)
    {
        return Element(page, name, value.ToString(CultureInfo.InvariantCulture));
    }

    public WbxmlWriter OpaqueElement(byte page, string name, ReadOnlySpan<byte> data)
    {
        WriteTag(page, name, hasContent: true);
        _stream.WriteByte(OpaqueToken);
        WriteMultiByteInt(data.Length);
        _stream.Write(data);
        _stream.WriteByte(EndToken);
        return this;
    }

    public byte[] ToArray()
    {
        if (_depth != 0)
        {
            throw new InvalidOperationException($"{_depth} element(s) left open.");
        }

        return _stream.ToArray();
    }

    private void WriteTag(byte page, string name, bool hasContent)
    {
        var token = WbxmlCodePages.GetToken(page, name);
        if (page != _currentPage)
        {
            _stream.WriteByte(SwitchPageToken);
            _stream.WriteByte(page);
            _currentPage = page;
        }

        _stream.WriteByte(hasContent ? (byte)(token | ContentFlag) : token);
    }

    private void WriteText(string value)
    {
        _stream.WriteByte(StrIToken);
        var bytes = Encoding.UTF8.GetBytes(value);
        _stream.Write(bytes, 0, bytes.Length);
        _stream.WriteByte(0x00);
    }

    private void WriteMultiByteInt(int value)
    {
        Span<byte> buffer = stackalloc byte[5];
        var index = 4;
        buffer[index] = (byte)(value & 0x7F);
        value >>= 7;
        while (value > 0)
        {
            index--;
            buffer[index] = (byte)((value & 0x7F) | 0x80);
            value >>= 7;
        }

        _stream.Write(buffer[index..]);
    }
}