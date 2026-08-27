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
        addGroup.Click += (_, _) => { var child = new ConditionGroupEditorRow(); child.Conditions.Add(new ConditionEditorRow()); child.Changed += () => Group.Changed?.Invoke(); Group.Groups.Add(child); Group.Changed?.Invoke(); Rebuild(); };
        header.Children.Add(addGroup);
        _content.Children.Add(header);
        foreach (var condition in Group.Conditions) _content.Children.Add(CreateCondition(condition));
        foreach (var child in Group.Groups)
        {
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
        var tags = new ComboBox { ItemsSource = ((MainWindowViewModel)Application.Current.MainWindow.DataContext).DicomTags, SelectedItem = condition.TagName };
        tags.SelectionChanged += (_, _) => { condition.TagName = tags.SelectedItem?.ToString() ?? condition.TagName; Group?.Changed?.Invoke(); };
        row.Children.Add(tags);
        var operators = new ComboBox { ItemsSource = Enum.GetValues<ConditionOperator>(), SelectedItem = condition.Operator, Margin = new Thickness(4, 0, 0, 0) };
        operators.SelectionChanged += (_, _) => { if (operators.SelectedItem is ConditionOperator value) { condition.Operator = value; Group?.Changed?.Invoke(); } };
        Grid.SetColumn(operators, 1); row.Children.Add(operators);
        var value = new ComboBox { IsEditable = true, Text = condition.Value, ItemsSource = condition.ValueOptions, Margin = new Thickness(4, 0, 0, 0) };
        value.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) => { condition.Value = value.Text; Group?.Changed?.Invoke(); }));
        Grid.SetColumn(value, 2); row.Children.Add(value);
        return row;
    }
}
