using System.Windows;
using System.Windows.Controls;

namespace buenoClothTool.Controls;

public partial class TagChip : UserControl
{
    public static readonly DependencyProperty TagTextProperty = 
        DependencyProperty.Register(
            nameof(TagText), 
            typeof(string), 
            typeof(TagChip), 
            new PropertyMetadata(string.Empty));

    public string TagText
    {
        get => (string)GetValue(TagTextProperty);
        set => SetValue(TagTextProperty, value);
    }

    public TagChip()
    {
        InitializeComponent();
    }
}
