using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace FaceLocker.Views.NumPad;

public partial class NumPadDialogView : UserControl
{
    public NumPadDialogView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}