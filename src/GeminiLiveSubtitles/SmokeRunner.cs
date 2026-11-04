using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using GeminiLiveSubtitles.Audio;
using GeminiLiveSubtitles.Gemini;
using GeminiLiveSubtitles.Settings;
using NAudio.Wave;

namespace GeminiLiveSubtitles;

/// <summary>
/// Headless verification mode: <c>GeminiLiveSubtitles.exe --smoke &lt;wav-file&gt; [target-lang]</c>.
/// Streams a WAV file through <see cref="AudioConverter"/> + <see cref="GeminiLiveClient"/> and prints
/// the resulting original and translated transcripts to stdout.
/// </summary>
internal static partial class SmokeRunner
{
    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(int dwProcessId);

    public static async Task<int> RunAsync(string[] args)
    {
        AttachConsole(-1);
        Log.EchoToConsole = true;

        var wavPath = args.Length > 1 ? args[1] : null;
        if (string.IsNullOrEmpty(wavPath) || !File.Exists(wavPath))
        {
            Console.Error.WriteLine("Usage: GeminiLiveSubtitles --smoke <wav-file> [target-lang]");
            return 2;
        }

        var settings = new SettingsService();
        var apiKey = settings.GetApiKey();
        if (string.IsNullOrEmpty(apiKey))
        {
            Console.Error.WriteLine("No API key configured (set GEMINI_API_KEY or configure in UI).");
            return 2;
        }

        var lang = args.Length > 2 ? args[2] : settings.Current.TargetLanguage;
        var outFile = args.Length > 3 ? args[3] : null;
        var logLines = new List<string>();
        void Out(string s)
        {
            Console.WriteLine(s);
            logLines.Add(s);
            if (outFile is not null) File.WriteAllLines(outFile, logLines);
        }

        var proxy = Environment.GetEnvironmentVariable("GEMINI_PROXY") ?? settings.Current.ProxyUrl;
        var opts = new GeminiOptions(apiKey, settings.Current.Model, settings.Current.ApiVersion, lang, proxy);

        var channel = Channel.CreateUnbounded<byte[]>();
        var client = new GeminiLiveClient(opts);
        var orig = new System.Text.StringBuilder();
        var trans = new System.Text.StringBuilder();
        var connected = new TaskCompletionSource();
        var done = new TaskCompletionSource();

        client.StateChanged += (st, msg) =>
        {
            Out($"[STATE] {st} {msg}");
            if (st == ConnectionState.Connected) connected.TrySetResult();
            else if (st == ConnectionState.Error) connected.TrySetException(new InvalidOperationException(msg ?? "Error"));
        };
        client.InputTranscript += t => { orig.Append(t); Out($"[ORIG ] {t}"); };
        client.OutputTranscript += t => { trans.Append(t); Out($"[TRANS] {t}"); };
        client.TurnComplete += () => { Out("[TURN ] complete"); done.TrySetResult(); };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var runTask = client.RunAsync(channel.Reader, cts.Token);

        await connected.Task.WaitAsync(cts.Token);

        // Stream WAV in real-time-ish 100 ms chunks, followed by 2 s of silence so VAD flushes.
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
                    await Task.Delay(50, cts.Token);
                }
            }
        }
        for (int i = 0; i < 25; i++)
        {
            await channel.Writer.WriteAsync(new byte[3200], cts.Token);
            await Task.Delay(60, cts.Token);
        }

        // Wait up to 12 s after audio finishes for the translation to complete.
        await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(12), cts.Token));
        cts.Cancel();
        try { await runTask; } catch (OperationCanceledException) { }

        Out($"=== SUMMARY ===");
        Out($"Original  : {orig}");
        Out($"Translated: {trans}");
        return trans.Length > 0 ? 0 : 1;
    }
}
