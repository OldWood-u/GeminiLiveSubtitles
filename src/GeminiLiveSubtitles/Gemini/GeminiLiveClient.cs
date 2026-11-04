using System.Buffers;
using System.Net;
using System.Net.WebSockets;
using System.Threading.Channels;

namespace GeminiLiveSubtitles.Gemini;

public enum ConnectionState
{
    Stopped,
    Connecting,
    Connected,
    Reconnecting,
    Error,
}

public sealed record GeminiOptions(string ApiKey, string Model, string ApiVersion, string TargetLanguage, string? ProxyUrl = null)
{
    public Uri Endpoint => new(
        $"wss://generativelanguage.googleapis.com/ws/google.ai.generativelanguage.{ApiVersion}.GenerativeService.BidiGenerateContent");
}

/// <summary>
/// Long-running Gemini Live session. Streams PCM chunks from a channel and raises
/// transcription events. Handles goAway / disconnects by reconnecting (with the latest
/// session-resumption handle when available) using exponential backoff.
/// </summary>
public sealed class GeminiLiveClient
{
    private static readonly TimeSpan SetupTimeout = TimeSpan.FromSeconds(15);
    private readonly GeminiOptions _options;
    private string? _resumptionHandle;
    private bool _sessionFeatures = true;
    private bool _bypassDefaultProxy;

    public GeminiLiveClient(GeminiOptions options) => _options = options;

    public event Action<string>? InputTranscript;
    public event Action<string>? OutputTranscript;
    public event Action? TurnComplete;
    public event Action<ConnectionState, string?>? StateChanged;

    /// <summary>Runs until <paramref name="ct"/> is cancelled.</summary>
    public async Task RunAsync(ChannelReader<byte[]> audio, CancellationToken ct)
    {
        var backoff = TimeSpan.FromSeconds(1);
        bool first = true;
        while (!ct.IsCancellationRequested)
        {
            StateChanged?.Invoke(first ? ConnectionState.Connecting : ConnectionState.Reconnecting, null);
            first = false;
            SessionResult result;
            try
            {
                result = await RunSessionAsync(audio, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Warn($"Live session failed: {ex.GetType().Name}: {ex.Message}");
                result = new SessionResult(false, false, ex.Message);
            }

            if (ct.IsCancellationRequested) break;

            if (result.Healthy)
            {
                backoff = TimeSpan.FromSeconds(1);
                if (result.ReconnectImmediately) continue;
            }
            else
            {
                StateChanged?.Invoke(ConnectionState.Error, result.Error);
            }

            try { await Task.Delay(backoff, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            backoff = TimeSpan.FromSeconds(Math.Min(30, backoff.TotalSeconds * 2));
        }
        StateChanged?.Invoke(ConnectionState.Stopped, null);
    }

    private readonly record struct SessionResult(bool Healthy, bool ReconnectImmediately, string? Error);

    private async Task<SessionResult> RunSessionAsync(ChannelReader<byte[]> audio, CancellationToken ct)
    {
        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("x-goog-api-key", _options.ApiKey);
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        if (_bypassDefaultProxy || string.Equals(_options.ProxyUrl?.Trim(), "none", StringComparison.OrdinalIgnoreCase))
            ws.Options.Proxy = null;
        else if (!string.IsNullOrWhiteSpace(_options.ProxyUrl))
            ws.Options.Proxy = new WebProxy(_options.ProxyUrl.Trim());

        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(SetupTimeout);
        await ws.ConnectAsync(_options.Endpoint, connectCts.Token).ConfigureAwait(false);

        var setup = LiveMessages.BuildSetup(_options.Model, _options.TargetLanguage, _resumptionHandle, _sessionFeatures);
        await ws.SendAsync(setup, WebSocketMessageType.Text, true, connectCts.Token).ConfigureAwait(false);
        Log.Info($"Setup sent (model={_options.Model}, lang={_options.TargetLanguage}, resume={_resumptionHandle is not null})");

        // Wait for setupComplete before streaming audio.
        var buffer = new ArrayBufferWriter<byte>(64 * 1024);
        while (true)
        {
            var msg = await ReceiveMessageAsync(ws, buffer, connectCts.Token).ConfigureAwait(false);
            if (msg is null) return HandleClose(ws, setupPhase: true);
            var events = LiveMessages.Parse(msg.Value);
            if (events.Any(e => e.Kind == ServerEventKind.SetupComplete)) break;
            Dispatch(events, out _);
        }

        Log.Info("Live session established");
        StateChanged?.Invoke(ConnectionState.Connected, null);

        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var sendTask = SendLoopAsync(ws, audio, sessionCts.Token);
        bool goAway = false;
        try
        {
            while (!sessionCts.IsCancellationRequested)
            {
                var msg = await ReceiveMessageAsync(ws, buffer, sessionCts.Token).ConfigureAwait(false);
                if (msg is null) break;
                Dispatch(LiveMessages.Parse(msg.Value), out var gotGoAway);
                if (gotGoAway)
                {
                    goAway = true;
                    break;
                }
                if (sendTask.IsFaulted) break;
            }
        }
        finally
        {
            sessionCts.Cancel();
            try { await sendTask.ConfigureAwait(false); } catch { /* reported below */ }
        }

        if (sendTask.IsFaulted)
            return new SessionResult(false, false, sendTask.Exception?.GetBaseException().Message);

        if (goAway)
        {
            Log.Info("goAway received; reconnecting with resumption handle");
            await TryCloseAsync(ws).ConfigureAwait(false);
            return new SessionResult(true, true, null);
        }

        if (ct.IsCancellationRequested)
        {
            await TryCloseAsync(ws).ConfigureAwait(false);
            return new SessionResult(true, false, null);
        }

        return HandleClose(ws, setupPhase: false);
    }

    private SessionResult HandleClose(ClientWebSocket ws, bool setupPhase)
    {
        var status = ws.CloseStatus;
        var desc = ws.CloseStatusDescription;
        Log.Warn($"Server closed connection: {status} {desc}");

        if (!string.IsNullOrEmpty(desc))
        {
            // Stale/invalid resumption handle -> start a fresh session.
            if (_resumptionHandle is not null && desc.Contains("resum", StringComparison.OrdinalIgnoreCase))
            {
                _resumptionHandle = null;
                return new SessionResult(true, true, null);
            }
            // Model doesn't accept sessionResumption / contextWindowCompression -> retry without.
            if (_sessionFeatures && setupPhase && desc.Contains("Unknown name", StringComparison.OrdinalIgnoreCase) &&
                (desc.Contains("sessionResumption") || desc.Contains("contextWindowCompression")))
            {
                _sessionFeatures = false;
                _resumptionHandle = null;
                return new SessionResult(true, true, null);
            }
            // Default proxy points to an unsupported region while TUN/direct works -> retry without default proxy.
            if (!_bypassDefaultProxy && string.IsNullOrWhiteSpace(_options.ProxyUrl) && setupPhase &&
                desc.Contains("location is not supported", StringComparison.OrdinalIgnoreCase))
            {
                Log.Info("Default proxy region unsupported; retrying with direct/TUN connection");
                _bypassDefaultProxy = true;
                return new SessionResult(true, true, null);
            }
        }

        // Normal closure after a healthy session (e.g. session time limit) -> reconnect quickly.
        if (!setupPhase && status is WebSocketCloseStatus.NormalClosure or null)
            return new SessionResult(true, true, null);

        return new SessionResult(false, false, string.IsNullOrEmpty(desc) ? status?.ToString() ?? "连接已断开" : desc);
    }

    private void Dispatch(List<ServerEvent> events, out bool goAway)
    {
        goAway = false;
        foreach (var e in events)
        {
            switch (e.Kind)
            {
                case ServerEventKind.InputTranscript: InputTranscript?.Invoke(e.Text!); break;
                case ServerEventKind.OutputTranscript: OutputTranscript?.Invoke(e.Text!); break;
                case ServerEventKind.TurnComplete: TurnComplete?.Invoke(); break;
                case ServerEventKind.ResumptionHandle: _resumptionHandle = e.Text; break;
                case ServerEventKind.GoAway:
                    Log.Info($"goAway (timeLeft={e.Text})");
                    goAway = true;
                    break;
                case ServerEventKind.Error: Log.Warn($"Server error: {e.Text}"); break;
            }
        }
    }

    private static async Task SendLoopAsync(ClientWebSocket ws, ChannelReader<byte[]> audio, CancellationToken ct)
    {
        try
        {
            await foreach (var chunk in audio.ReadAllAsync(ct).ConfigureAwait(false))
            {
                if (ws.State != WebSocketState.Open) return;
                var json = LiveMessages.BuildAudio(chunk);
                await ws.SendAsync(json, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    /// <summary>Reads one complete (possibly fragmented) message. Returns null when the socket closes.</summary>
    private static async Task<ReadOnlyMemory<byte>?> ReceiveMessageAsync(ClientWebSocket ws, ArrayBufferWriter<byte> buffer, CancellationToken ct)
    {
        buffer.Clear();
        while (true)
        {
            var mem = buffer.GetMemory(16 * 1024);
            ValueWebSocketReceiveResult r;
            try
            {
                r = await ws.ReceiveAsync(mem, ct).ConfigureAwait(false);
            }
            catch (WebSocketException) when (ws.State is WebSocketState.Closed or WebSocketState.CloseReceived or WebSocketState.Aborted)
            {
                return null;
            }
            if (r.MessageType == WebSocketMessageType.Close) return null;
            buffer.Advance(r.Count);
            if (r.EndOfMessage) return buffer.WrittenMemory;
        }
    }

    private static async Task TryCloseAsync(ClientWebSocket ws)
    {
        if (ws.State is not (WebSocketState.Open or WebSocketState.CloseReceived)) return;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token).ConfigureAwait(false);
        }
        catch { /* ignore */ }
    }
}
