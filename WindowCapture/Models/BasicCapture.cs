using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.IO;
using WindowCapture.Helpers;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;
using Windows.UI.Composition;

namespace WindowCapture.Models {

    public sealed class SurfaceWithInfo {
        public IDirect3DSurface? Surface { get; internal set; }
        public TimeSpan SystemRelativeTime { get; internal set; }
    }

    public sealed class AudioWithInfo {
        public IBuffer? buff { get; internal set; }
        public TimeSpan SystemRelativeTime { get; internal set; }
    }

    internal class BasicCapture : IDisposable {
        private const int BUFFER_COUNT = 2;
        private const DirectXPixelFormat DX_PIX_FMT = DirectXPixelFormat.B8G8R8A8UIntNormalized;
        private const SharpDX.DXGI.Format SDX_PIX_FMT = SharpDX.DXGI.Format.B8G8R8A8_UNorm;
        private readonly uint _sampleRate;
        private readonly uint _bitsPerSample;

        private readonly ManualResetEvent _frameEvent = new(false);
        private readonly ManualResetEvent _audioEvent = new(false);
        private readonly ManualResetEvent _closedEvent = new(false);
        private readonly ManualResetEvent[] _wait_frame_events;
        private readonly ManualResetEvent[] _wait_audio_events;

        private readonly SharpDX.Direct3D11.Texture2D _blankTexture;
        private readonly GraphicsCaptureItem item;
        private readonly Direct3D11CaptureFramePool framePool;
        private readonly GraphicsCaptureSession session;

        private SizeInt32 lastSize;
        private readonly IDirect3DDevice device;
        private readonly SharpDX.Direct3D11.Device d3dDevice_encode;
        private readonly SharpDX.Direct3D11.Device d3dDevice_preview;
        private readonly SharpDX.DXGI.SwapChain1 swapChain;
        private WasapiLoopbackCapture loopbackCapture = new();

        private readonly Queue<SurfaceWithInfo> surfaces = new();
        private readonly Queue<AudioWithInfo> pcms = new();
        private DateTime _startTime = DateTime.Now;

        public BasicCapture(IDirect3DDevice d, GraphicsCaptureItem i, uint sampleRate, uint bitsPerSample) {
            _sampleRate = sampleRate;
            _bitsPerSample = bitsPerSample;
            _wait_frame_events = new[] { _closedEvent, _frameEvent };
            _wait_audio_events = new[] { _closedEvent, _audioEvent };
            item = i;
            device = d;
            d3dDevice_encode = Direct3D11Helper.CreateSharpDXDevice(device);
            d3dDevice_preview = Direct3D11Helper.CreateSharpDXDevice(device);


            swapChain = InitSwapChain();

            framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            device,
            DX_PIX_FMT,
            BUFFER_COUNT,
            i.Size);

            session = framePool.CreateCaptureSession(i);
            framePool.FrameArrived += OnFrameArrived;
            loopbackCapture.DataAvailable += OnDataArrived;
            lastSize = i.Size;
            _blankTexture = InitializeBlankTexture(i.Size);
        }

        public void Dispose() {
            StopCapture();
            session.Dispose();
            framePool.Dispose();
            swapChain.Dispose();
            d3dDevice_encode.Dispose();
            d3dDevice_preview.Dispose();
        }

        public void StartCapture() {
            _startTime = DateTime.Now;
            session.StartCapture();
            loopbackCapture.StartRecording();
        }

        public void StopCapture() {
            _closedEvent.Set();
            loopbackCapture.StopRecording();
        }

        public ICompositionSurface CreateSurface(Compositor compositor) {
            return compositor.CreateCompositionSurfaceForSwapChain(swapChain);
        }

        private byte[] ToPCM(byte[] inputBuffer, int length, WaveFormat format) {
            if (length == 0)
                return new byte[0]; // No bytes recorded, return empty array.

            // Create a WaveStream from the input buffer.
            using var memStream = new MemoryStream(inputBuffer, 0, length);
            using var inputStream = new RawSourceWaveStream(memStream, format);

            byte[] convertedBuffer = new byte[length];

            using var stream = new MemoryStream();
            int read;

            var sample_provider = new WdlResamplingSampleProvider(new WaveToSampleProvider(inputStream), (int)_sampleRate);
            if (_bitsPerSample == 16) {
                var convertedPCM = new SampleToWaveProvider16(sample_provider);
                // Read the converted WaveProvider into a buffer and turn it into a Stream.
                while ((read = convertedPCM.Read(convertedBuffer, 0, length)) > 0)
                    stream.Write(convertedBuffer, 0, read);
            } else if (_bitsPerSample == 24) {
                var convertedPCM = new SampleToWaveProvider24(sample_provider);
                // Read the converted WaveProvider into a buffer and turn it into a Stream.
                while ((read = convertedPCM.Read(convertedBuffer, 0, length)) > 0)
                    stream.Write(convertedBuffer, 0, read);
            } else {
                throw new InvalidOperationException();
            }

            // Return the converted Stream as a byte array.
            return stream.ToArray();
        }
        private void OnDataArrived(object? sender, WaveInEventArgs a) {
            var info = new AudioWithInfo {
                buff = CryptographicBuffer.CreateFromByteArray(ToPCM(a.Buffer, a.BytesRecorded, loopbackCapture.WaveFormat)),
                SystemRelativeTime = DateTime.Now - _startTime
            };
            pcms.Enqueue(info);
            _audioEvent.Set();
        }

        private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args) {
            using var frame = sender.TryGetNextFrame();
            if (frame == null) {
                return;
            }
            SetSurface(frame);
            SetPreview(frame);
        }

        private void SetSurface(Direct3D11CaptureFrame frame) {
            using var bitmap = Direct3D11Helper.CreateSharpDXTexture2D(frame.Surface);
            var result = new SurfaceWithInfo {
                SystemRelativeTime = frame.SystemRelativeTime
            };
            var description = bitmap.Description;
            description.Usage = SharpDX.Direct3D11.ResourceUsage.Default;
            description.BindFlags = SharpDX.Direct3D11.BindFlags.ShaderResource | SharpDX.Direct3D11.BindFlags.RenderTarget;
            description.CpuAccessFlags = SharpDX.Direct3D11.CpuAccessFlags.None;
            description.OptionFlags = SharpDX.Direct3D11.ResourceOptionFlags.None;

            using var copyTexture = new SharpDX.Direct3D11.Texture2D(d3dDevice_encode, description);
            var width = Math.Clamp(frame.ContentSize.Width, 0, frame.Surface.Description.Width);
            var height = Math.Clamp(frame.ContentSize.Height, 0, frame.Surface.Description.Height);

            var region = new SharpDX.Direct3D11.ResourceRegion(0, 0, 0, width, height, 1);
            d3dDevice_encode?.ImmediateContext.CopyResource(_blankTexture, copyTexture);
            d3dDevice_encode?.ImmediateContext.CopySubresourceRegion(bitmap, 0, region, copyTexture, 0);
            result.Surface = Direct3D11Helper.CreateDirect3DSurfaceFromSharpDXTexture(copyTexture);
            surfaces.Enqueue(result);
            _frameEvent.Set();
        }

        public void SetPreview(Direct3D11CaptureFrame frame) {
            var newSize = false;
            if (frame.ContentSize.Width != lastSize.Width ||
               frame.ContentSize.Height != lastSize.Height) {
                // The thing we have been capturing has changed size.
                // We need to resize the swap chain first, then blit the pixels.
                // After we do that, retire the frame and then recreate the frame pool.
                newSize = true;
                lastSize = frame.ContentSize;
                swapChain.ResizeBuffers(
                    BUFFER_COUNT,
                    lastSize.Width,
                    lastSize.Height,
                    SDX_PIX_FMT,
                     SharpDX.DXGI.SwapChainFlags.HwProtected);
            }

            using var bitmap = Direct3D11Helper.CreateSharpDXTexture2D(frame.Surface);
            using var backBuffer = swapChain.GetBackBuffer<SharpDX.Direct3D11.Texture2D>(0);
            d3dDevice_preview?.ImmediateContext.CopyResource(bitmap, backBuffer);
            swapChain.Present(0, SharpDX.DXGI.PresentFlags.None);
            if (newSize) {
                framePool.Recreate(
                    device,
                    DX_PIX_FMT,
                    BUFFER_COUNT,
                    lastSize);
            }
        }

        private SharpDX.Direct3D11.Texture2D InitializeBlankTexture(SizeInt32 size) {
            var description = new SharpDX.Direct3D11.Texture2DDescription {
                Width = size.Width,
                Height = size.Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = SDX_PIX_FMT,
                SampleDescription = new SharpDX.DXGI.SampleDescription() {
                    Count = 1,
                    Quality = 0
                },
                Usage = SharpDX.Direct3D11.ResourceUsage.Default,
                BindFlags = SharpDX.Direct3D11.BindFlags.ShaderResource | SharpDX.Direct3D11.BindFlags.RenderTarget,
                CpuAccessFlags = SharpDX.Direct3D11.CpuAccessFlags.None,
                OptionFlags = SharpDX.Direct3D11.ResourceOptionFlags.None
            };
            var texture = new SharpDX.Direct3D11.Texture2D(d3dDevice_encode, description);

            using var renderTargetView = new SharpDX.Direct3D11.RenderTargetView(d3dDevice_encode, texture);
            d3dDevice_encode?.ImmediateContext.ClearRenderTargetView(renderTargetView, new SharpDX.Mathematics.Interop.RawColor4(0, 0, 0, 1));
            return texture;
        }

        private SharpDX.DXGI.SwapChain1 InitSwapChain() {
            var dxgiFactory = new SharpDX.DXGI.Factory2();
            var description = new SharpDX.DXGI.SwapChainDescription1() {
                Width = item.Size.Width,
                Height = item.Size.Height,
                Format = SDX_PIX_FMT,
                Stereo = false,
                SampleDescription = new SharpDX.DXGI.SampleDescription() {
                    Count = 1,
                    Quality = 0
                },
                Usage = SharpDX.DXGI.Usage.RenderTargetOutput,
                BufferCount = BUFFER_COUNT,
                Scaling = SharpDX.DXGI.Scaling.Stretch,
                SwapEffect = SharpDX.DXGI.SwapEffect.FlipSequential,
                AlphaMode = SharpDX.DXGI.AlphaMode.Premultiplied,
                Flags = SharpDX.DXGI.SwapChainFlags.None
            };
            return new SharpDX.DXGI.SwapChain1(dxgiFactory, d3dDevice_preview, ref description);
        }

        public SurfaceWithInfo? WaitForNewFrame() {
            if (surfaces.Count == 0) {
                _frameEvent.Reset();
                var signaledEvent = _wait_frame_events[WaitHandle.WaitAny(_wait_frame_events)];
                if (signaledEvent == _closedEvent) {
                    StopCapture();
                    return null;
                }
            }
            var surface = surfaces.Dequeue();
            return surface;
        }

        public AudioWithInfo? WaitForNewPcm() {
            if (pcms.Count == 0) {
                _audioEvent.Reset();
                var signaledEvent = _wait_audio_events[WaitHandle.WaitAny(_wait_audio_events)];
                if (signaledEvent == _closedEvent) {
                    StopCapture();
                    return null;
                }
            }
            var pcm = pcms.Dequeue();
            return pcm;
        }
    }
}
