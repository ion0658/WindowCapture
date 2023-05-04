using System.Windows;
using System.Windows.Interop;
using WindowCapture.ViewModels;

namespace WindowCapture {
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window {
        private readonly MainWindowViewModel viewModel = new();

        public MainWindow() {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e) {
            var interopWindow = new WindowInteropHelper(this);
            var hwnd = interopWindow.Handle;

            var presentationSource = PresentationSource.FromVisual(this);
            double dpiX = 1.0;
            if (presentationSource != null) {
                dpiX = presentationSource.CompositionTarget.TransformToDevice.M11;
            }
            var controlsWidth = (float)(ControlsGrid.ActualWidth * dpiX);

            viewModel.InitComposition(hwnd, controlsWidth);
            Closing += (e, s) => { viewModel.Dispose(); };
        }


    }
}
