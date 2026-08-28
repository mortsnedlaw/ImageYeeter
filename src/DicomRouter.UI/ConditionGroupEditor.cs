using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DicomRouter.Core.Models;

namespace DicomRouter.UI;

public sealed class ConditionGroupEditor : Border
{
    private readonly StackPanel _content = new();
    public ConditionGroupEditor()
    {
        Background = new SolidColorBrush(Color.FromRgb(30, 41, 49));
        BorderBrush = new SolidColorBrush(Color.FromRgb(58, 81, 94));
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(7);
        Padding = new Thickness(8);
        DataContextChanged += (_, _) => Rebuild();
    }

    private ConditionGroupEditorRow? Group => DataContext as ConditionGroupEditorRow;
    private void Rebuild()
    {
        _content.Children.Clear();
        Child = _content;
        if (Group == null) return;
        var header = new DockPanel();
        var operatorBox = new ComboBox { Width = 82, ItemsSource = Enum.GetValues<ConditionGroupOperator>(), SelectedItem = Group.Operator };
        operatorBox.SelectionChanged += (_, _) => { if (operatorBox.SelectedItem is ConditionGroupOperator value) { Group.Operator = value; Group.Changed?.Invoke(); } };
        header.Children.Add(operatorBox);
        var not = new CheckBox { Content = "NOT", Margin = new Thickness(8, 4, 0, 0), IsChecked = Group.Negate, Foreground = Brushes.White };
        not.Checked += (_, _) => { Group.Negate = true; Group.Changed?.Invoke(); };
        not.Unchecked += (_, _) => { Group.Negate = false; Group.Changed?.Invoke(); };
        header.Children.Add(not);
        var addCondition = new Button { Content = "+ condition", Padding = new Thickness(7, 3, 7, 3), Margin = new Thickness(8, 0, 0, 0) };
        addCondition.Click += (_, _) => { Group.Conditions.Add(new ConditionEditorRow()); Group.Changed?.Invoke(); Rebuild(); };
        header.Children.Add(addCondition);
        var addGroup = new Button { Content = "+ group", Padding = new Thickness(7, 3, 7, 3), Margin = new Thickness(5, 0, 0, 0) };
        addGroup.Click += (_, _) => { var child = new ConditionGroupEditorRow { Parent = Group }; child.Conditions.Add(new ConditionEditorRow()); child.Changed += () => Group.Changed?.Invoke(); Group.Groups.Add(child); Group.Changed?.Invoke(); Rebuild(); };
        header.Children.Add(addGroup);
        if (Group.Parent != null)
        {
            var removeGroup = new Button { Content = "remove group", Padding = new Thickness(7, 3, 7, 3), Margin = new Thickness(5, 0, 0, 0) };
            removeGroup.Click += (_, _) => { Group.Parent.Groups.Remove(Group); Group.Parent.Changed?.Invoke(); };
            header.Children.Add(removeGroup);
        }
        _content.Children.Add(header);
        foreach (var condition in Group.Conditions) _content.Children.Add(CreateCondition(condition));
        foreach (var child in Group.Groups)
        {
            child.Parent = Group;
            child.Changed += () => Group.Changed?.Invoke();
            var nested = new ConditionGroupEditor { DataContext = child, Margin = new Thickness(18, 6, 0, 0) };
            _content.Children.Add(nested);
        }
    }

    private UIElement CreateCondition(ConditionEditorRow condition)
    {
        var row = new Grid { Margin = new Thickness(0, 5, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var viewModel = Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault()?.DataContext as MainWindowViewModel;
        var tags = new ComboBox { ItemsSource = viewModel?.DicomTags ?? Array.Empty<string>(), SelectedItem = condition.TagName };
        ApplyDarkComboBoxStyle(tags);
        tags.SelectionChanged += (_, _) => { condition.TagName = tags.SelectedItem?.ToString() ?? condition.TagName; Group?.Changed?.Invoke(); };
        row.Children.Add(tags);
        var operators = new ComboBox { ItemsSource = Enum.GetValues<ConditionOperator>(), SelectedItem = condition.Operator, Margin = new Thickness(4, 0, 0, 0) };
        ApplyDarkComboBoxStyle(operators);
        operators.SelectionChanged += (_, _) => { if (operators.SelectedItem is ConditionOperator value) { condition.Operator = value; Group?.Changed?.Invoke(); } };
        Grid.SetColumn(operators, 1); row.Children.Add(operators);
        var value = new ComboBox { IsEditable = true, Text = condition.Value, ItemsSource = condition.ValueOptions, Margin = new Thickness(4, 0, 0, 0) };
        ApplyDarkComboBoxStyle(value);
        value.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) => { condition.Value = value.Text; Group?.Changed?.Invoke(); }));
        Grid.SetColumn(value, 2); row.Children.Add(value);
        var remove = new Button { Content = "x", Width = 24, Padding = new Thickness(0), Margin = new Thickness(4, 0, 0, 0) };
        remove.Click += (_, _) => { Group?.Conditions.Remove(condition); Group?.Changed?.Invoke(); Rebuild(); };
        Grid.SetColumn(remove, 3);
        row.Children.Add(remove);
        return row;
    }

    private static void ApplyDarkComboBoxStyle(ComboBox comboBox)
    {
        var style = new Style(typeof(ComboBox));
        style.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(17, 24, 29))));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(231, 237, 242))));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(52, 70, 80))));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(5)));
        var focused = new Trigger { Property = UIElement.IsFocusedProperty, Value = true };
        focused.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(85, 214, 160))));
        focused.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        style.Triggers.Add(focused);
        comboBox.Style = style;

        var itemStyle = new Style(typeof(ComboBoxItem));
        itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(17, 24, 29))));
        itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(231, 237, 242))));
        comboBox.Resources[typeof(ComboBoxItem)] = itemStyle;

        var textStyle = new Style(typeof(TextBox));
        textStyle.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(17, 24, 29))));
        textStyle.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(231, 237, 242))));
        textStyle.Setters.Add(new Setter(TextBox.CaretBrushProperty, Brushes.White));
        comboBox.Resources[typeof(TextBox)] = textStyle;
    }
}
