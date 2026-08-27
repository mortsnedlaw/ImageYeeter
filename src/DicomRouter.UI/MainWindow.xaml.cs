using System;
using System.Windows;

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
            if (FindName("RouteGraph") is RouteGraphCanvas graph)
                graph.EdgeCreated = edge => { if (!_viewModel.GraphEdges.Any(x => x.FromNodeId == edge.FromNodeId && x.ToNodeId == edge.ToNodeId)) _viewModel.GraphEdges.Add(edge); };
        }

        protected override void OnClosed(EventArgs e)
        {
            _viewModel.Dispose();
            base.OnClosed(e);
        }
    }
}
