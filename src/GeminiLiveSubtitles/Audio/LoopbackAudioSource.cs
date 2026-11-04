using System.Buffers;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace GeminiLiveSubtitles.Audio;

/// <summary>
/// Captures whatever the default Windows render device is playing (WASAPI loopback),
/// converts it to 16 kHz mono PCM16 and emits 100 ms chunks. Automatically follows
/// default-device changes (e.g. plugging in headphones) and recovers from capture errors.
/// </summary>
public sealed class LoopbackAudioSource : IDisposable
{
    private readonly object _gate = new();
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly DeviceNotificationClient _notifier;
    private readonly ArrayBufferWriter<byte> _pcm = new(16 * 1024);
    private WasapiLoopbackCapture? _capture;
    private AudioConverter? _converter;
    private PcmChunker? _chunker;
    private bool _running;
    private Timer? _restartTimer;

    public LoopbackAudioSource()
    {
        _notifier = new DeviceNotificationClient(this);
        _enumerator.RegisterEndpointNotificationCallback(_notifier);
    }

    /// <summary>Raised on the capture thread with a 100 ms PCM16 16 kHz mono chunk.</summary>
    public event Action<byte[]>? ChunkAvailable;

    public string? DeviceName { get; private set; }

    public void Start()
    {
        lock (_gate)
        {
            if (_running) return;
            _running = true;
            StartCaptureLocked();
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _running = false;
            StopCaptureLocked();
        }
    }

    private void StartCaptureLocked()
    {
        try
        {
            var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            DeviceName = device.FriendlyName;
            var capture = new WasapiLoopbackCapture(device);
            _converter = AudioConverter.FromWaveFormat(capture.WaveFormat);
            _chunker = new PcmChunker();
            capture.DataAvailable += OnDataAvailable;
            capture.RecordingStopped += OnRecordingStopped;
            _capture = capture;
            capture.StartRecording();
            Log.Info($"Loopback capture started on '{DeviceName}' ({capture.WaveFormat})");
        }
        catch (Exception ex)
        {
            Log.Error("Failed to start loopback capture", ex);
            ScheduleRestart(TimeSpan.FromSeconds(3));
        }
    }

    private void StopCaptureLocked()
    {
        var c = _capture;
        _capture = null;
        if (c is null) return;
        c.DataAvailable -= OnDataAvailable;
        c.RecordingStopped -= OnRecordingStopped;
        try { c.StopRecording(); } catch { /* ignore */ }
        c.Dispose();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0 || !ReferenceEquals(sender, _capture)) return;
        var conv = _converter;
        var chunker = _chunker;
        if (conv is null || chunker is null) return;
        _pcm.Clear();
        conv.Convert(e.Buffer.AsSpan(0, e.BytesRecorded), _pcm);
        chunker.Push(_pcm.WrittenSpan, chunk => ChunkAvailable?.Invoke(chunk));
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            Log.Warn($"Loopback capture stopped with error: {e.Exception.Message}");
            ScheduleRestart(TimeSpan.FromSeconds(1));
        }
    }

    /// <summary>Debounced restart (device notifications often arrive in bursts).</summary>
    private void ScheduleRestart(TimeSpan delay)
    {
        lock (_gate)
        {
            if (!_running) return;
            _restartTimer?.Dispose();
            _restartTimer = new Timer(_ =>
            {
                lock (_gate)
                {
                    if (!_running) return;
                    StopCaptureLocked();
                    StartCaptureLocked();
                }
            }, null, delay, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        Stop();
        _restartTimer?.Dispose();
        try { _enumerator.UnregisterEndpointNotificationCallback(_notifier); } catch { /* ignore */ }
        _enumerator.Dispose();
    }

    private sealed class DeviceNotificationClient(LoopbackAudioSource owner) : IMMNotificationClient
    {
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow == DataFlow.Render && role == Role.Multimedia)
            {
                Log.Info("Default render device changed; restarting capture");
                owner.ScheduleRestart(TimeSpan.FromMilliseconds(500));
            }
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
        public void OnDeviceAdded(string pwstrDeviceId) { }
        public void OnDeviceRemoved(string deviceId) { }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
}
