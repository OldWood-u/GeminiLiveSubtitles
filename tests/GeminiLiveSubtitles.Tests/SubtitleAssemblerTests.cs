using GeminiLiveSubtitles.Subtitles;
using Microsoft.Extensions.Time.Testing;

namespace GeminiLiveSubtitles.Tests;

public class SubtitleAssemblerTests
{
    private readonly FakeTimeProvider _time = new();

    [Fact]
    public void Splits_On_Cjk_Sentence_End()
    {
        var t = new SubtitleTrack(_time);
        t.Append("今天天气很好。");
        Assert.Equal("", t.Previous);          // terminator at end: wait for more text
        t.Append("我们去公园吧");
        Assert.Equal("今天天气很好。", t.Previous);
        Assert.Equal("我们去公园吧", t.Current);
        Assert.Equal("今天天气很好。\n我们去公园吧", t.DisplayText);
    }

    [Fact]
    public void Splits_On_Latin_Sentence_End_Only_Before_Whitespace()
    {
        var t = new SubtitleTrack(_time);
        t.Append("Pi is 3.");
        t.Append("14 exactly");
        Assert.Equal("", t.Previous);
        Assert.Equal("Pi is 3.14 exactly", t.Current);
        t.Append(". Next one");
        Assert.Equal("Pi is 3.14 exactly.", t.Previous);
        Assert.Equal("Next one", t.Current);
    }

    [Fact]
    public void Keeps_Closing_Quotes_With_Sentence()
    {
        var t = new SubtitleTrack(_time);
        t.Append("他说：“走吧。”然后离开了");
        Assert.Equal("他说：“走吧。”", t.Previous);
        Assert.Equal("然后离开了", t.Current);
    }

    [Fact]
    public void Breaks_Long_Cjk_Line_By_Width()
    {
        var t = new SubtitleTrack(_time, maxWidth: 20); // 10 CJK chars
        t.Append("一二三四五六七八九十十一十二");
        Assert.Equal("一二三四五六七八九十", t.Previous);
        Assert.Equal("十一十二", t.Current);
    }

    [Fact]
    public void Breaks_Long_Latin_Line_At_Space()
    {
        var t = new SubtitleTrack(_time, maxWidth: 20);
        t.Append("the quick brown fox jumps over");
        Assert.Equal("the quick brown fox", t.Previous);
        Assert.Equal("jumps over", t.Current);
    }

    [Fact]
    public void Commits_After_Pause_Then_Clears_After_Silence()
    {
        var t = new SubtitleTrack(_time);
        t.Append("Hello there");
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.False(t.Tick());
        _time.Advance(TimeSpan.FromSeconds(0.6));
        Assert.True(t.Tick());
        Assert.Equal("Hello there", t.Previous);
        Assert.Equal("", t.Current);
        _time.Advance(TimeSpan.FromSeconds(4));
        Assert.True(t.Tick());
        Assert.Equal("", t.DisplayText);
        Assert.False(t.Tick());
    }

    [Fact]
    public void TurnComplete_Commits_Both_Tracks_And_Raises_Changed()
    {
        var a = new SubtitleAssembler(_time);
        int changes = 0;
        a.Changed += () => changes++;
        a.AppendOriginal("Good morning");
        a.AppendTranslated("早上好");
        a.TurnComplete();
        Assert.Equal(3, changes);
        Assert.Equal("Good morning", a.Original.DisplayText);
        Assert.Equal("早上好", a.Translated.DisplayText);
        Assert.Equal("", a.Translated.Current);
    }

    [Fact]
    public void Leading_Whitespace_Is_Trimmed_On_New_Line()
    {
        var t = new SubtitleTrack(_time);
        Assert.False(t.Append("   "));
        t.Append(" Hi");
        Assert.Equal("Hi", t.Current);
    }

    [Fact]
    public void HistoryEntries_Accumulate_Across_Turns_Without_Disappearing()
    {
        var a = new SubtitleAssembler(_time);
        a.AppendOriginal("First sentence.");
        a.AppendTranslated("第一句话。");
        a.TurnComplete();

        a.AppendOriginal("Second sentence.");
        a.AppendTranslated("第二句话。");

        Assert.Equal(2, a.Entries.Count);
        Assert.False(a.Entries[0].IsActive);
        Assert.Equal("First sentence.", a.Entries[0].Original);
        Assert.Equal("第一句话。", a.Entries[0].Translated);

        Assert.True(a.Entries[1].IsActive);
        Assert.Equal("Second sentence.", a.Entries[1].Original);
        Assert.Equal("第二句话。", a.Entries[1].Translated);

        // Even after a long silence, history entries remain visible (never auto-cleared).
        _time.Advance(TimeSpan.FromSeconds(30));
        a.Tick();
        Assert.Equal(2, a.Entries.Count);
        Assert.False(a.Entries[1].IsActive);
    }

    [Fact]
    public void HistoryEntries_Split_When_New_Sentence_Starts_After_Punctuation()
    {
        var a = new SubtitleAssembler(_time);
        a.AppendOriginal("Hello world.");
        a.AppendTranslated("你好世界。");
        a.AppendOriginal("How are you?");
        a.AppendTranslated("你好吗？");

        Assert.Equal(2, a.Entries.Count);
        Assert.Equal("你好世界。", a.Entries[0].Translated);
        Assert.Equal("你好吗？", a.Entries[1].Translated);
    }
}
