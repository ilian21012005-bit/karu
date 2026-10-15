using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Karu.App;

/// <summary>Pastille « i » : l’explication est le tooltip de <see cref="Hint"/>.</summary>
public class InfoHint : Button
{
    public static readonly DependencyProperty HintProperty = DependencyProperty.Register(
        nameof(Hint),
        typeof(string),
        typeof(InfoHint),
        new PropertyMetadata("", OnHintChanged));

    public InfoHint()
    {
        Loaded += (_, _) => RefreshTip();
    }

    public string Hint
    {
        get => (string)GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    private static void OnHintChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((InfoHint)d).RefreshTip();
    }

    private void RefreshTip()
    {
        var text = Hint ?? "";
        AutomationProperties.SetHelpText(this, text);
        AutomationProperties.SetName(this, "Information");

        if (string.IsNullOrWhiteSpace(text))
        {
            ToolTip = null;
            return;
        }

        var tip = new ToolTip
        {
            Content = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 300
            }
        };
        if (TryFindResource("SettingInfoTip") is Style style)
        {
            tip.Style = style;
        }

        ToolTip = tip;
    }
}
