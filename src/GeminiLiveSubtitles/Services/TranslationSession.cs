using System.Threading.Channels;
using GeminiLiveSubtitles.Audio;
using GeminiLiveSubtitles.Gemini;

namespace GeminiLiveSubtitles.Services;

/// <summary>
/// Wires loopback capture → bounded channel → Gemini Live client. Events are raised
/// on background threads; consumers marshal to the UI thread themselves.
/// </summary>
public sealed class TranslationSession : IDisposable
{
    private readonly LoopbackAudioSource _audio = new();
    private Channel<byte[]>? _channel;
    private CancellationTokenSource? _cts;
    private Task? _runTask;

    public TranslationSession()
    {
        _audio.ChunkAvailable += chunk => _channel?.Writer.TryWrite(chunk);
    }

    public event Action<string>? InputTranscript;
    public event Action<string>? OutputTranscript;
    public event Action? TurnComplete;
    public event Action<ConnectionState, string?>? StateChanged;

    public bool IsRunning => _cts is not null;

    public void Start(GeminiOptions options)
    {
        if (IsRunning) return;
        // ~5 s of audio; on network stalls drop the oldest audio rather than accumulate latency.
        _channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(50)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true,
        });
        _cts = new CancellationTokenSource();

        var client = new GeminiLiveClient(options);
        client.InputTranscript += t => InputTranscript?.Invoke(t);
        client.OutputTranscript += t => OutputTranscript?.Invoke(t);
        client.TurnComplete += () => TurnComplete?.Invoke();
        client.StateChanged += (s, m) => StateChanged?.Invoke(s, m);

        _audio.Start();
        var reader = _channel.Reader;
        var token = _cts.Token;
        _runTask = Task.Run(() => client.RunAsync(reader, token));
        Log.Info("Translation session started");
    }

    public void Stop()
    {
        if (!IsRunning) return;
        _audio.Stop();
        _cts!.Cancel();
        _channel?.Writer.TryComplete();
        try { _runTask?.Wait(TimeSpan.FromSeconds(3)); } catch { /* ignore */ }
        _cts.Dispose();
        _cts = null;
        _channel = null;
        _runTask = null;
        Log.Info("Translation session stopped");
    }

    public void Restart(GeminiOptions options)
    {
        Stop();
        Start(options);
    }

    public void Dispose()
    {
        Stop();
        _audio.Dispose();
    }
}
