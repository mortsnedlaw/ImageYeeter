using System;
using System.Windows;
using System.Windows.Controls;

namespace DicomRouter.UI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainWindowViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainWindowViewModel();
            DataContext = _viewModel;
            AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler(WireDestinationEchoButton), true);
            ConfigureRuleGrid();
            if (FindName("RouteGraph") is RouteGraphCanvas graph)
            {
                graph.EdgeCreated = _viewModel.ConnectEdge;
                graph.EdgeDeleted = _viewModel.RemoveEdge;
                }
        }

        private void WireDestinationEchoButton(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is Button button && string.Equals(button.Content?.ToString(), "C-ECHO", StringComparison.OrdinalIgnoreCase))
            {
                button.Command = _viewModel.TestDestinationEchoCommand;
                button.CommandParameter = button.DataContext;
            }
        }

        private void ConfigureRuleGrid()
        {
            var rulesTab = FindVisualChild<TabItem>(this, item => Equals(item.Header, "RULES"));
            var grid = rulesTab == null ? null : FindVisualChild<DataGrid>(rulesTab, _ => true);
            if (grid == null || grid.Columns.Count < 4) return;
            grid.Columns.RemoveAt(3);
            var factory = new FrameworkElementFactory(typeof(ConditionGroupEditor));
            factory.SetBinding(FrameworkElement.DataContextProperty, new System.Windows.Data.Binding("RootGroup"));
            var template = new DataTemplate { VisualTree = factory };
            grid.Columns.Insert(3, new DataGridTemplateColumn { Header = "CONDITIONS", CellTemplate = template, Width = new DataGridLength(4, DataGridLengthUnitType.Star) });
        }

        private static T? FindVisualChild<T>(DependencyObject root, Func<T, bool> predicate) where T : DependencyObject
        {
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            {
                if (child is T match && predicate(match)) return match;
                var nested = FindVisualChild(child, predicate);
                if (nested != null) return nested;
            }
            return null;
        }

        protected override void OnClosed(EventArgs e)
        {
            _viewModel.Dispose();
            base.OnClosed(e);
        }
    }
}
