using System.Text.Json;
using System.Text.Json.Serialization;

namespace GeminiLiveSubtitles.Gemini;

// ---------------- Client -> Server ----------------

internal sealed class SetupEnvelope
{
    public required SetupMessage Setup { get; init; }
}

internal sealed class SetupMessage
{
    public required string Model { get; init; }
    public required GenerationConfig GenerationConfig { get; init; }
    public EmptyObject? InputAudioTranscription { get; init; }
    public EmptyObject? OutputAudioTranscription { get; init; }
    public SessionResumptionConfig? SessionResumption { get; init; }
    public ContextWindowCompression? ContextWindowCompression { get; init; }
}

internal sealed class GenerationConfig
{
    public string[] ResponseModalities { get; init; } = ["AUDIO"];
    public TranslationConfig? TranslationConfig { get; init; }
}

internal sealed class TranslationConfig
{
    public required string TargetLanguageCode { get; init; }
    public bool EchoTargetLanguage { get; init; }
}

internal sealed class SessionResumptionConfig
{
    public string? Handle { get; init; }
}

internal sealed class ContextWindowCompression
{
    public EmptyObject SlidingWindow { get; init; } = new();
}

internal sealed class EmptyObject;

internal sealed class RealtimeInputEnvelope
{
    public required RealtimeInput RealtimeInput { get; init; }
}

internal sealed class RealtimeInput
{
    public Blob? Audio { get; init; }
    public bool? AudioStreamEnd { get; init; }
}

internal sealed class Blob
{
    public required string MimeType { get; init; }
    public required string Data { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SetupEnvelope))]
[JsonSerializable(typeof(RealtimeInputEnvelope))]
internal partial class LiveJsonContext : JsonSerializerContext;

// ---------------- Server -> Client ----------------

internal enum ServerEventKind
{
    SetupComplete,
    InputTranscript,
    OutputTranscript,
    TurnComplete,
    Interrupted,
    GoAway,
    ResumptionHandle,
    Error,
}

internal readonly record struct ServerEvent(ServerEventKind Kind, string? Text = null);

internal static class LiveMessages
{
    public const string AudioMimeType = "audio/pcm;rate=16000";

    public static byte[] BuildSetup(string model, string targetLanguage, string? resumptionHandle, bool includeSessionFeatures = true)
    {
        var setup = new SetupEnvelope
        {
            Setup = new SetupMessage
            {
                Model = model.StartsWith("models/", StringComparison.Ordinal) ? model : "models/" + model,
                GenerationConfig = new GenerationConfig
                {
                    TranslationConfig = new TranslationConfig { TargetLanguageCode = targetLanguage, EchoTargetLanguage = false },
                },
                InputAudioTranscription = new EmptyObject(),
                OutputAudioTranscription = new EmptyObject(),
                SessionResumption = includeSessionFeatures ? new SessionResumptionConfig { Handle = resumptionHandle } : null,
                ContextWindowCompression = includeSessionFeatures ? new ContextWindowCompression() : null,
            },
        };
        return JsonSerializer.SerializeToUtf8Bytes(setup, LiveJsonContext.Default.SetupEnvelope);
    }

    public static byte[] BuildAudio(ReadOnlySpan<byte> pcm16)
    {
        var msg = new RealtimeInputEnvelope
        {
            RealtimeInput = new RealtimeInput { Audio = new Blob { MimeType = AudioMimeType, Data = Convert.ToBase64String(pcm16) } },
        };
        return JsonSerializer.SerializeToUtf8Bytes(msg, LiveJsonContext.Default.RealtimeInputEnvelope);
    }

    public static byte[] BuildAudioStreamEnd() =>
        JsonSerializer.SerializeToUtf8Bytes(
            new RealtimeInputEnvelope { RealtimeInput = new RealtimeInput { AudioStreamEnd = true } },
            LiveJsonContext.Default.RealtimeInputEnvelope);

    /// <summary>
    /// Parses one server message into zero or more events. Audio payloads in modelTurn are
    /// intentionally ignored (never base64-decoded) because we only display subtitles.
    /// </summary>
    public static List<ServerEvent> Parse(ReadOnlyMemory<byte> utf8Json)
    {
        var events = new List<ServerEvent>(2);
        using var doc = JsonDocument.Parse(utf8Json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return events;

        if (root.TryGetProperty("setupComplete", out _))
            events.Add(new(ServerEventKind.SetupComplete));

        if (root.TryGetProperty("serverContent", out var sc) && sc.ValueKind == JsonValueKind.Object)
        {
            if (TryGetText(sc, "inputTranscription", out var input))
                events.Add(new(ServerEventKind.InputTranscript, input));
            if (TryGetText(sc, "outputTranscription", out var output))
                events.Add(new(ServerEventKind.OutputTranscript, output));
            if (sc.TryGetProperty("interrupted", out var intr) && intr.ValueKind == JsonValueKind.True)
                events.Add(new(ServerEventKind.Interrupted));
            if (sc.TryGetProperty("turnComplete", out var tc) && tc.ValueKind == JsonValueKind.True)
                events.Add(new(ServerEventKind.TurnComplete));
        }

        if (root.TryGetProperty("goAway", out var ga))
        {
            string? timeLeft = ga.ValueKind == JsonValueKind.Object && ga.TryGetProperty("timeLeft", out var tl) ? tl.ToString() : null;
            events.Add(new(ServerEventKind.GoAway, timeLeft));
        }

        if (root.TryGetProperty("sessionResumptionUpdate", out var sru) && sru.ValueKind == JsonValueKind.Object)
        {
            var resumable = !sru.TryGetProperty("resumable", out var r) || r.ValueKind != JsonValueKind.False;
            if (resumable && sru.TryGetProperty("newHandle", out var h) && h.ValueKind == JsonValueKind.String)
                events.Add(new(ServerEventKind.ResumptionHandle, h.GetString()));
        }

        if (root.TryGetProperty("error", out var err))
            events.Add(new(ServerEventKind.Error, err.ToString()));

        return events;
    }

    private static bool TryGetText(JsonElement parent, string name, out string text)
    {
        text = "";
        if (parent.TryGetProperty(name, out var t) && t.ValueKind == JsonValueKind.Object &&
            t.TryGetProperty("text", out var s) && s.ValueKind == JsonValueKind.String)
        {
            text = s.GetString() ?? "";
            return text.Length > 0;
        }
        return false;
    }
}
