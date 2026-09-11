using System.Windows;
using System.Windows.Controls;

namespace OwaWidget.App.Views;

public sealed class StreamTemplateSelector : DataTemplateSelector
{
    public DataTemplate? SectionTemplate { get; set; }

    public DataTemplate? EventTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        return item switch
        {
            StreamSection => SectionTemplate,
            StreamEvent => EventTemplate,
            _ => base.SelectTemplate(item, container)
        };
    }
}