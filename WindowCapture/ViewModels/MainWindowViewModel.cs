﻿using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using System.Windows;
using WindowCapture.Helpers;
using WindowCapture.Models;
using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;
using Windows.UI.Composition;

namespace WindowCapture.ViewModels {

    [TypeConverter(typeof(EnumDescriptionTypeConverter))]
    public enum TargetProcs {
        [Description("League of Legends")]
        League_Of_Legends,
    }

    internal partial class MainWindowViewModel : ObservableObject, IDisposable {
        private const int POOLING_DUR_MSEC = 5_000;

        private SettingStore _settingStore = SettingStore.Instance;

        [ObservableProperty]
        private bool _isAutoRecording = false;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EnableProcSelector))]
        private bool _recording = false;
        [ObservableProperty]
        private TargetProcs _selectedProc = TargetProcs.League_Of_Legends;

        public string SaveFolderName { get => _settingStore.GetVideoPath(); }

        public bool EnableProcSelector => !Recording;


        private CaptureView? sample;
        private Compositor? compositor;
        private CompositionTarget? target;
        private ContainerVisual? root;
        private GraphicsCaptureItem? item;

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
                item = CaptureHelper.CreateItemForWindow(hwnd);
                Debug.WriteLine($"Proc: {proc.ProcessName}");
                if (sample != null && item != null) {
                    Recording = true;
                    item.Closed += (s, e) => {
                        Debug.WriteLine("aaa");
                        AutoRecordChanged();
                    };
                    await sample.StartCaptureFromItem(item, SaveFolderName, GetProcName(SelectedProc));
                }
            });
        }

        private static string GetProcName(TargetProcs proc) {
            return proc switch {
                TargetProcs.League_Of_Legends => "League of Legends",
                _ => throw new NotImplementedException()
            };
        }

        [RelayCommand]
        public void StopButton() {
            Application.Current.Dispatcher.Invoke(() => {
                Debug.WriteLine("Stop Button");
                sample?.StopCapture();
                item = null;
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
        public async void ChangeSaveDir() {
            var browser = new FolderBrowserDialog {
                Title = "Select Save Folder",
                SelectedPath = SaveFolderName
            };
            var result = browser.ShowDialog(IntPtr.Zero);
            if (result == DialogResult.OK) {
                await _settingStore.SaveVideoPathAsync(browser.SelectedPath);
                OnPropertyChanged(nameof(SaveFolderName));
            }
        }

        public void Dispose() {
            IsAutoRecording = false;
            StopButton();
        }
    }
}
