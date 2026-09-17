using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace OwaWidget.App.Views;

public static class Highlight
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text",
        typeof(string),
        typeof(Highlight),
        new PropertyMetadata(string.Empty, OnChanged));

    public static readonly DependencyProperty WordsProperty = DependencyProperty.RegisterAttached(
        "Words",
        typeof(IReadOnlyList<string>),
        typeof(Highlight),
        new PropertyMetadata(null, OnChanged));

    public static void SetText(DependencyObject element, string value)
    {
        element.SetValue(TextProperty, value);
    }

    public static string GetText(DependencyObject element)
    {
        return (string)element.GetValue(TextProperty);
    }

    public static void SetWords(DependencyObject element, IReadOnlyList<string>? value)
    {
        element.SetValue(WordsProperty, value);
    }

    public static IReadOnlyList<string>? GetWords(DependencyObject element)
    {
        return (IReadOnlyList<string>?)element.GetValue(WordsProperty);
    }

    private static void OnChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not TextBlock block)
        {
            return;
        }

        var text = GetText(block) ?? string.Empty;
        var words = GetWords(block);

        block.Inlines.Clear();

        if (words is null || words.Count == 0 || text.Length == 0)
        {
            block.Inlines.Add(new Run(text));
            return;
        }

        var marked = Mark(text, words);
        var start = 0;

        for (var i = 1; i <= text.Length; i++)
        {
            if (i < text.Length && marked[i] == marked[start])
            {
                continue;
            }

            var run = new Run(text[start..i]);

            if (marked[start])
            {
                run.Background = Palette.Mark;
            }

            block.Inlines.Add(run);
            start = i;
        }
    }

    private static bool[] Mark(string text, IReadOnlyList<string> words)
    {
        var marked = new bool[text.Length];
        var lower = text.ToLowerInvariant();

        if (lower.Length != text.Length)
        {
            return marked;
        }

        foreach (var word in words)
        {
            if (word.Length == 0)
            {
                continue;
            }

            var index = lower.IndexOf(word, StringComparison.Ordinal);

            while (index >= 0)
            {
                if (index == 0 || !char.IsLetterOrDigit(lower[index - 1]))
                {
                    for (var i = index; i < index + word.Length; i++)
                    {
                        marked[i] = true;
                    }
                }

                index = lower.IndexOf(word, index + 1, StringComparison.Ordinal);
            }
        }

        return marked;
    }
}