using System.Buffers;
using System.IO;
using System.Threading.Channels;
using GeminiLiveSubtitles.Audio;
using GeminiLiveSubtitles.Gemini;
using NAudio.Wave;
using Xunit.Abstractions;

namespace GeminiLiveSubtitles.Tests;

public class LiveApiSmokeTests(ITestOutputHelper output)
{
    [Fact]
    public async Task LiveTranslate_StreamsWav_AndReturnsChineseSubtitles()
    {
        var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        var wavPath = Environment.GetEnvironmentVariable("SMOKE_WAV");
        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(wavPath) || !File.Exists(wavPath))
        {
            output.WriteLine("Skipped (GEMINI_API_KEY / SMOKE_WAV not set).");
            return;
        }

        var proxy = Environment.GetEnvironmentVariable("GEMINI_PROXY");
        var opts = new GeminiOptions(apiKey, "gemini-3.5-live-translate-preview", "v1beta", "zh-CN", proxy);
        var channel = Channel.CreateUnbounded<byte[]>();
        var client = new GeminiLiveClient(opts);
        var orig = new System.Text.StringBuilder();
        var trans = new System.Text.StringBuilder();
        var connected = new TaskCompletionSource();
        var done = new TaskCompletionSource();

        client.StateChanged += (st, msg) =>
        {
            output.WriteLine($"[STATE] {st} {msg}");
            if (st == ConnectionState.Connected) connected.TrySetResult();
            else if (st == ConnectionState.Error) connected.TrySetException(new InvalidOperationException(msg ?? "Error"));
        };
        client.InputTranscript += t => { orig.Append(t); output.WriteLine($"[ORIG ] {t}"); };
        client.OutputTranscript += t => { trans.Append(t); output.WriteLine($"[TRANS] {t}"); };
        client.TurnComplete += () => { output.WriteLine("[TURN ] complete"); done.TrySetResult(); };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var runTask = client.RunAsync(channel.Reader, cts.Token);

        await connected.Task.WaitAsync(cts.Token);

        using (var reader = new WaveFileReader(wavPath))
        {
            var conv = AudioConverter.FromWaveFormat(reader.WaveFormat);
            var chunker = new PcmChunker(maxSilentChunks: 100);
            var pcm = new ArrayBufferWriter<byte>(8192);
            var raw = new byte[reader.WaveFormat.AverageBytesPerSecond / 10];
            int n;
            while ((n = reader.Read(raw, 0, raw.Length)) > 0)
            {
                pcm.Clear();
                conv.Convert(raw.AsSpan(0, n), pcm);
                var chunks = new List<byte[]>();
                chunker.Push(pcm.WrittenSpan, chunks.Add);
                foreach (var c in chunks)
                {
                    await channel.Writer.WriteAsync(c, cts.Token);
                    await Task.Delay(60, cts.Token);
                }
            }
        }
        for (int i = 0; i < 25; i++)
        {
            await channel.Writer.WriteAsync(new byte[3200], cts.Token);
            await Task.Delay(60, cts.Token);
        }

        await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(12), cts.Token));
        cts.Cancel();
        try { await runTask; } catch (OperationCanceledException) { }

        output.WriteLine($"Original  : {orig}");
        output.WriteLine($"Translated: {trans}");
        Assert.NotEmpty(orig.ToString().Trim());
        Assert.NotEmpty(trans.ToString().Trim());
    }
}
