using System.Windows;
using System.Windows.Controls;

namespace OwaWidget.App.Views;

public sealed class StreamTemplateSelector : DataTemplateSelector
{
    public DataTemplate? SectionTemplate { get; set; }

    public DataTemplate? MeetingTemplate { get; set; }

    public DataTemplate? MailTemplate { get; set; }

    public DataTemplate? MailListTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        return item switch
        {
            StreamSection => SectionTemplate,
            StreamMeeting => MeetingTemplate,
            StreamMail => MailTemplate,
            MailListRow => MailListTemplate,
            _ => base.SelectTemplate(item, container)
        };
    }
}