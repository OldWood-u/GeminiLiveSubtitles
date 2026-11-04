using System.Text;
using System.Text.Json;
using GeminiLiveSubtitles.Gemini;

namespace GeminiLiveSubtitles.Tests;

public class LiveMessagesTests
{
    [Fact]
    public void Setup_PutsTranslationConfigInsideGenerationConfig()
    {
        var json = LiveMessages.BuildSetup("gemini-3.5-live-translate-preview", "zh-CN", null);
        using var doc = JsonDocument.Parse(json);
        var setup = doc.RootElement.GetProperty("setup");

        Assert.Equal("models/gemini-3.5-live-translate-preview", setup.GetProperty("model").GetString());
        var gen = setup.GetProperty("generationConfig");
        Assert.Equal("AUDIO", gen.GetProperty("responseModalities")[0].GetString());
        var tc = gen.GetProperty("translationConfig");
        Assert.Equal("zh-CN", tc.GetProperty("targetLanguageCode").GetString());
        Assert.False(tc.GetProperty("echoTargetLanguage").GetBoolean());
        Assert.Equal(JsonValueKind.Object, setup.GetProperty("inputAudioTranscription").ValueKind);
        Assert.Equal(JsonValueKind.Object, setup.GetProperty("outputAudioTranscription").ValueKind);
        Assert.Equal(JsonValueKind.Object, setup.GetProperty("sessionResumption").ValueKind);
        Assert.False(setup.GetProperty("sessionResumption").TryGetProperty("handle", out _));
        Assert.Equal(JsonValueKind.Object, setup.GetProperty("contextWindowCompression").GetProperty("slidingWindow").ValueKind);
        Assert.False(setup.TryGetProperty("translationConfig", out _));
    }

    [Fact]
    public void Setup_IncludesResumptionHandle_AndCanOmitSessionFeatures()
    {
        using (var doc = JsonDocument.Parse(LiveMessages.BuildSetup("models/x", "ja", "abc")))
            Assert.Equal("abc", doc.RootElement.GetProperty("setup").GetProperty("sessionResumption").GetProperty("handle").GetString());

        using (var doc = JsonDocument.Parse(LiveMessages.BuildSetup("models/x", "ja", null, includeSessionFeatures: false)))
        {
            var setup = doc.RootElement.GetProperty("setup");
            Assert.Equal("models/x", setup.GetProperty("model").GetString());
            Assert.False(setup.TryGetProperty("sessionResumption", out _));
            Assert.False(setup.TryGetProperty("contextWindowCompression", out _));
        }
    }

    [Fact]
    public void Audio_IsBase64Pcm16k()
    {
        var json = LiveMessages.BuildAudio(new byte[] { 1, 2, 3, 4 });
        using var doc = JsonDocument.Parse(json);
        var audio = doc.RootElement.GetProperty("realtimeInput").GetProperty("audio");
        Assert.Equal("audio/pcm;rate=16000", audio.GetProperty("mimeType").GetString());
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, Convert.FromBase64String(audio.GetProperty("data").GetString()!));
    }

    private static List<ServerEvent> P(string json) => LiveMessages.Parse(Encoding.UTF8.GetBytes(json));

    [Fact]
    public void Parses_SetupComplete() =>
        Assert.Equal(ServerEventKind.SetupComplete, Assert.Single(P("""{ "setupComplete": {} }""")).Kind);

    [Fact]
    public void Parses_Transcriptions_AndIgnoresAudio()
    {
        var events = P("""
        {"serverContent":{
            "modelTurn":{"parts":[{"inlineData":{"mimeType":"audio/pcm;rate=24000","data":"AAAAAAAA"}}]},
            "inputTranscription":{"text":"Hello "},
            "outputTranscription":{"text":"你好"},
            "turnComplete":true}}
        """);
        Assert.Equal(
            [new ServerEvent(ServerEventKind.InputTranscript, "Hello "), new ServerEvent(ServerEventKind.OutputTranscript, "你好"), new ServerEvent(ServerEventKind.TurnComplete)],
            events);
    }

    [Fact]
    public void Parses_GoAway_And_ResumptionHandle()
    {
        Assert.Equal(new ServerEvent(ServerEventKind.GoAway, "10s"), Assert.Single(P("""{"goAway":{"timeLeft":"10s"}}""")));
        Assert.Equal(new ServerEvent(ServerEventKind.ResumptionHandle, "h1"),
            Assert.Single(P("""{"sessionResumptionUpdate":{"newHandle":"h1","resumable":true}}""")));
        Assert.Empty(P("""{"sessionResumptionUpdate":{"newHandle":"h2","resumable":false}}"""));
        Assert.Empty(P("""{"serverContent":{"outputTranscription":{"text":""}}}"""));
        Assert.Empty(P("""{"usageMetadata":{"totalTokenCount":5}}"""));
    }
}
