using System.Windows;
using WindowCapture.Helpers;
using Windows.System;

namespace WindowCapture {
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application {
        private DispatcherQueueController _queueController;
        public App() {
            _queueController = CoreMessagingHelper.CreateDispatcherQueueControllerForCurrentThread()!;
        }
    }
}
