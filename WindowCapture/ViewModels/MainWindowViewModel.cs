using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using System.Windows;
using WindowCapture.Helpers;
using WindowCapture.Models;
using Windows.Foundation.Metadata;
using Windows.UI.Composition;

namespace WindowCapture.ViewModels {

    [TypeConverter(typeof(EnumDescriptionTypeConverter))]
    public enum TargetProcs {
        [Description("Explorer")]
        Explorer,
        [Description("Firefox")]
        Firefox,
        [Description("League of Legends")]
        League_Of_Legends,
    }

    internal partial class MainWindowViewModel : ObservableObject, IDisposable {
        private const int POOLING_DUR_MSEC = 1_000;
        [ObservableProperty]
        private bool _isAutoRecording = false;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EnableProcSelector))]
        private bool _recording = false;
        [ObservableProperty]
        private TargetProcs _selectedProc = TargetProcs.Explorer;
        [ObservableProperty]
        private string _saveFolderName = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

        public bool EnableProcSelector => !Recording;


        private CaptureView? sample;
        private Compositor? compositor;
        private CompositionTarget? target;
        private ContainerVisual? root;

        public void InitComposition(IntPtr hwnd, float controlWidth) {
            compositor = new Compositor();
            // Create a target for the window.
            target = compositor.CreateDesktopWindowTarget(hwnd, true);

            // Attach the root visual.
            root = compositor.CreateContainerVisual();
            root.RelativeSizeAdjustment = Vector2.One;
            root.Size = new Vector2(-controlWidth, 0);
            root.Offset = new Vector3(controlWidth, 0, 0);
            target.Root = root;

            // Setup the rest of the sample application.
            sample = new CaptureView(compositor);
            root.Children.InsertAtTop(sample.Visual);
            Task.Run(SearchLoop);
        }

        private async Task SearchLoop() {
            try {
                if (IsAutoRecording && !Recording && ApiInformation.IsApiContractPresent(typeof(Windows.Foundation.UniversalApiContract).FullName, 8)) {
                    var processesWithWindows = from p in Process.GetProcesses()
                                               where !string.IsNullOrWhiteSpace(p.MainWindowTitle)
                                                     && WindowEnumerationHelper.IsWindowValidForCapture(p.MainWindowHandle)
                                               select p;
#if DEBUG
                    foreach (var processes in processesWithWindows) {
                        Debug.WriteLine(processes.ProcessName);
                    }
#endif
                    var target_proc_name = GetProcName(SelectedProc);
                    if (processesWithWindows.Any((p) => p.ProcessName.Equals(target_proc_name))) {
                        var capture_proc = processesWithWindows.Where((p) => p.ProcessName.Equals(target_proc_name)).First();
                        if (capture_proc != null) {
                            StartHwndCapture(capture_proc);
                        }
                    }
                }
            } catch (Exception ex) {
                Debug.WriteLine(ex.Message);
            } finally {
                await Task.Delay(POOLING_DUR_MSEC);
                await SearchLoop();
            }
        }

        private void StartHwndCapture(Process proc) {
            Application.Current.Dispatcher.Invoke(async () => {
                var hwnd = proc.MainWindowHandle;
                var item = CaptureHelper.CreateItemForWindow(hwnd);
                Debug.WriteLine($"Proc: {proc.ProcessName}");
                if (sample != null && item != null) {
                    Recording = true;
                    await sample.StartCaptureFromItem(item, SaveFolderName, GetProcName(SelectedProc));
                    item.Closed += (self, e) => { StopButton(); };
                }
            });
        }

        private static string GetProcName(TargetProcs proc) {
            return proc switch {
                TargetProcs.Explorer => "explorer",
                TargetProcs.Firefox => "firefox",
                TargetProcs.League_Of_Legends => "leagueoflegends",
                _ => throw new NotImplementedException()
            };
        }

        [RelayCommand]
        public void StopButton() {
            Application.Current.Dispatcher.Invoke(() => {
                sample?.StopCapture();
                Recording = false;
            });
        }

        [RelayCommand]
        public void AutoRecordChanged() {
            if (Recording) {
                StopButton();
            }
        }

        [RelayCommand]
        public void ChangeSaveDir() {
            var browser = new FolderBrowserDialog {
                Title = "Select Save Folder",
                SelectedPath = SaveFolderName
            };
            var result = browser.ShowDialog(IntPtr.Zero);
            if (result == DialogResult.OK) {
                SaveFolderName = browser.SelectedPath;
            }
        }

        public void Dispose() {
            IsAutoRecording = false;
            StopButton();
        }
    }
}
