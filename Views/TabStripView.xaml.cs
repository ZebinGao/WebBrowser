using System.Windows.Controls;

namespace WebBrowser.Views;

/// <summary>
/// Horizontal tab strip: an <see cref="ItemsControl"/> of tabs (each a select button with a close overlay)
/// plus a "new tab" button. Pure view — all interaction is via commands bound to <see cref="ViewModels.MainViewModel"/>.
/// </summary>
public partial class TabStripView : UserControl
{
    public TabStripView()
    {
        InitializeComponent();
    }
}
