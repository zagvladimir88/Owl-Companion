namespace OwaWidget.Eas.Wbxml;

public sealed class WbxmlElement
{
    public WbxmlElement(byte codePage, string name)
    {
        CodePage = codePage;
        Name = name;
    }

    public byte CodePage { get; }

    public string Name { get; }

    public string? Text { get; set; }

    public byte[]? OpaqueData { get; set; }

    public List<WbxmlElement> Children { get; } = new();

    public WbxmlElement? Child(string name)
    {
        foreach (var child in Children)
        {
            if (string.Equals(child.Name, name, StringComparison.Ordinal))
            {
                return child;
            }
        }

        return null;
    }

    public string? ChildText(string name)
    {
        return Child(name)?.Text;
    }

    public int? ChildInt(string name)
    {
        var text = ChildText(name);
        return int.TryParse(text, out var value) ? value : null;
    }

    public IEnumerable<WbxmlElement> ChildrenNamed(string name)
    {
        foreach (var child in Children)
        {
            if (string.Equals(child.Name, name, StringComparison.Ordinal))
            {
                yield return child;
            }
        }
    }

    public WbxmlElement? Descendant(params string[] path)
    {
        var current = this;
        foreach (var name in path)
        {
            current = current.Child(name);
            if (current is null)
            {
                return null;
            }
        }

        return current;
    }

    public override string ToString()
    {
        return Text is null ? Name : $"{Name}={Text}";
    }
}