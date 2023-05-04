using System.Diagnostics;
using System.IO;
using System.Numerics;
using WindowCapture.Helpers;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Media.MediaProperties;
using Windows.Storage;
using Windows.UI.Composition;

namespace WindowCapture.Models {
    internal class CaptureView : IDisposable {
        const uint sampleRate = 48_000;
        const uint bitsPerSample = 16;
        const VideoEncodingQuality v_quality = VideoEncodingQuality.HD1080p;
        private readonly Compositor compositor;
        private readonly ContainerVisual root;

        private readonly SpriteVisual content;
        private readonly CompositionSurfaceBrush brush;

        private readonly IDirect3DDevice device;
        private BasicCapture? capture;
        private Encoder? encoder;

        public CaptureView(Compositor c) {
            compositor = c;
            device = Direct3D11Helper.CreateDevice()!;

            // Setup the root.
            root = compositor.CreateContainerVisual();
            root.RelativeSizeAdjustment = Vector2.One;

            // Setup the content.
            brush = compositor.CreateSurfaceBrush();
            brush.HorizontalAlignmentRatio = 0.5f;
            brush.VerticalAlignmentRatio = 0.5f;
            brush.Stretch = CompositionStretch.Uniform;

            var shadow = compositor.CreateDropShadow();
            shadow.Mask = brush;

            content = compositor.CreateSpriteVisual();
            content.AnchorPoint = new Vector2(0.5f);
            content.RelativeOffsetAdjustment = new Vector3(0.5f, 0.5f, 0);
            content.RelativeSizeAdjustment = Vector2.One;
            content.Size = new Vector2(-80, -80);
            content.Brush = brush;
            content.Shadow = shadow;
            root.Children.InsertAtTop(content);
        }

        public Visual Visual => root;

        public void Dispose() {
            StopCapture();
            root.Dispose();
            content.Dispose();
            brush.Dispose();
            device?.Dispose();
            encoder?.Dispose();
        }

        public async Task StartCaptureFromItem(GraphicsCaptureItem item, string folder_path, string proc_name) {
            StopCapture();
            capture = new BasicCapture(device, item, sampleRate, bitsPerSample);
            encoder = new Encoder(capture, item.Size.Width, item.Size.Height, sampleRate, bitsPerSample);
            try {
                var surface = capture?.CreateSurface(compositor);
                brush.Surface = surface;
                capture?.StartCapture();

                var file = await GetTempFileAsync(folder_path, proc_name);
                using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
                await encoder.EncodeAsync(stream, MediaEncodingProfile.CreateMp4(v_quality));

            } catch (Exception ex) {
                Debug.WriteLine($"{ex.Message}\n {ex.StackTrace}");
            }
        }

        private async Task<StorageFile> GetTempFileAsync(string folder_path, string proc_name) {
            var dir_name = $"{folder_path}\\{proc_name}";
            if (!Directory.Exists(dir_name)) {
                Directory.CreateDirectory(dir_name);
            }
            var folder = await StorageFolder.GetFolderFromPathAsync(dir_name);
            var date = DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss");
            var file = await folder.CreateFileAsync($"{proc_name}_{date}.mp4");
            return file;
        }

        public void StopCapture() {
            capture?.StopCapture();
            brush.Surface = null;
        }
    }
}
