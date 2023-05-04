using System.Diagnostics;
using Windows.Foundation;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage.Streams;

namespace WindowCapture.Models {
    internal class Encoder {
        private readonly BasicCapture _capture;
        private MediaStreamSource _mediaStreamSource;
        private MediaTranscoder _transcoder;
        private bool _isRecording = false;
        private bool _closed = false;

        public Encoder(BasicCapture capture, int width, int height, uint sampleRate, uint bitsPerSample) {
            _capture = capture;
            // Describe our input: uncompressed BGRA8 buffers
            var videoProperties = VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Bgra8, (uint)width, (uint)height);
            var videoDescriptor = new VideoStreamDescriptor(videoProperties) {
                Name = "video"
            };
            var audioProperties = AudioEncodingProperties.CreatePcm(sampleRate, 2, bitsPerSample);
            var audioDescriptor = new AudioStreamDescriptor(audioProperties) {
                Name = "audio"
            };
            // Create our MediaStreamSource
            _mediaStreamSource = new MediaStreamSource(videoDescriptor, audioDescriptor) {
                BufferTime = TimeSpan.FromSeconds(0),
            };
            _mediaStreamSource.Starting += OnMediaStreamSourceStarting;
            _mediaStreamSource.SampleRequested += OnMediaStreamSourceSampleRequested;


            // Create our transcoder
            _transcoder = new MediaTranscoder {
                HardwareAccelerationEnabled = true,
                VideoProcessingAlgorithm = MediaVideoProcessingAlgorithm.Default,
                AlwaysReencode = true,
                TrimStartTime = TimeSpan.Zero,
                TrimStopTime = TimeSpan.Zero,
            };
        }


        private void OnMediaStreamSourceSampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args) {
            if (_isRecording && !_closed) {

                try {
                    if (args.Request.StreamDescriptor.Name.Equals("video")) {
                        var frame = _capture.WaitForNewFrame();
                        if (frame == null) {
                            Debug.WriteLine("video request null");
                            args.Request.Sample = null;
                            DisposeInternal();
                            return;
                        }
                        args.Request.Sample = frame;
                    } else if (args.Request.StreamDescriptor.Name.Equals("audio")) {
                        var frame = _capture.WaitForNewPcm();
                        if (frame == null) {
                            Debug.WriteLine("audio request null");
                            args.Request.Sample = null;
                            DisposeInternal();
                            return;
                        }
                        args.Request.Sample = frame;
                    } else {
                        Debug.WriteLine("other request");
                        args.Request.Sample = null;
                        DisposeInternal();
                        return;
                    }
                } catch (Exception e) {
                    Debug.WriteLine(e.Message);
                    Debug.WriteLine(e.StackTrace);
                    Debug.WriteLine(e);
                    args.Request.Sample = null;
                    DisposeInternal();
                }
            } else {
                args.Request.Sample = null;
                DisposeInternal();
            }
        }


        private void OnMediaStreamSourceStarting(MediaStreamSource sender, MediaStreamSourceStartingEventArgs args) {
            var frame = _capture.WaitForNewFrame();
            if (frame == null) {
                DisposeInternal();
                return;
            }
            args.Request.SetActualStartPosition(frame.Timestamp);
        }

        public IAsyncAction EncodeAsync(IRandomAccessStream stream, MediaEncodingProfile encodingProfile) {
            return EncodeInternalAsync(stream, encodingProfile).AsAsyncAction();
        }

        private async Task EncodeInternalAsync(IRandomAccessStream stream, MediaEncodingProfile encodingProfile) {
            if (!_isRecording) {
                _isRecording = true;
                Debug.WriteLine($"bitrate:{encodingProfile.Video.Bitrate}, framerate: {encodingProfile.Video.FrameRate}, size: {encodingProfile.Video.Width}x{encodingProfile.Video.Height}");
                Debug.WriteLine($"AudioSubtype:{encodingProfile.Audio.Subtype}, BitsPerSample: {encodingProfile.Audio.BitsPerSample}");
                encodingProfile.Video.FrameRate.Numerator = 60;
                encodingProfile.Video.FrameRate.Denominator = 1;
                var transcode = await _transcoder.PrepareMediaStreamSourceTranscodeAsync(_mediaStreamSource, stream, encodingProfile);
                await transcode.TranscodeAsync();
            }
        }

        private void DisposeInternal() {
            _capture.StopCapture();
        }

        public void Dispose() {
            if (_closed) {
                return;
            }
            _closed = true;

            if (!_isRecording) {
                DisposeInternal();
            }

            _isRecording = false;
        }
    }
}
