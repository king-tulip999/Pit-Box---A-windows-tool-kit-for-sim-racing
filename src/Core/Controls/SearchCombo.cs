using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Core.Views;

namespace Core.Controls;

internal sealed class SearchCombo : ComboBox
{
    private object? _before;
    private readonly TextBox _search = new() { Tag = "Search…", Margin = new Thickness(2, 2, 2, 5) };

    public SearchCombo()
    {
        SetResourceReference(StyleProperty, typeof(ComboBox));
        IsTextSearchEnabled = false;
        _search.TextChanged += (_, _) => ApplyFilter();
        _search.PreviewKeyDown += OnSearchKey;
        DropDownOpened += (_, _) =>
        {
            _before = SelectedItem;
            Dispatcher.BeginInvoke(new Action(() => { _search.Focus(); Keyboard.Focus(_search); }), System.Windows.Threading.DispatcherPriority.Input);
        };
        DropDownClosed += (_, _) =>
        {
            _search.Text = "";
            if (SelectedItem is null && _before is not null && Items.Contains(_before)) SelectedItem = _before;
            _before = null;
        };
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("PART_Popup") is not Popup { Child: Border { Child: DockPanel card } } || card.Children.OfType<ScrollViewer>().FirstOrDefault() is not { } list) return;
        var head = new Grid();
        head.Children.Add(_search);
        Ui.SearchFace(_search);
        head.Margin = new Thickness(3, 3, 3, 0);
        DockPanel.SetDock(head, Dock.Top);
        card.Children.Insert(card.Children.IndexOf(list), head);
    }

    private void ApplyFilter()
    {
        var words = _search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) { Items.Filter = null; return; }
        object? first = Items.Count > 0 ? Items.GetItemAt(0) : null;
        Items.Filter = item => item == first || item is Choice c && words.All(w => c.Text.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    private void OnSearchKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                var match = _search.Text.Trim().Length > 0 ? Items.Cast<object>().Skip(1).FirstOrDefault() : null;
                if (match is not null) SelectedItem = match;
                IsDropDownOpen = false;
                e.Handled = true;
                break;
            case Key.Escape:
                IsDropDownOpen = false;
                e.Handled = true;
                break;
            case Key.Down when ItemContainerGenerator.ContainerFromIndex(0) is ComboBoxItem item:
                item.Focus();
                e.Handled = true;
                break;
        }
    }
}
