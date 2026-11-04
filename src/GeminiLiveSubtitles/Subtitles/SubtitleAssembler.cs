using System.Collections.ObjectModel;
using System.Text;

namespace GeminiLiveSubtitles.Subtitles;

/// <summary>
/// Turns a stream of incremental transcript fragments into at most two display lines
/// (previous sentence + current sentence). Breaks on sentence punctuation, on a max
/// display width, and after a pause. Clears the screen after a longer silence.
/// </summary>
public sealed class SubtitleTrack
{
    private readonly TimeProvider _time;
    private readonly StringBuilder _current = new();
    private DateTimeOffset _lastUpdate;

    public SubtitleTrack(TimeProvider? time = null, int maxWidth = 60)
    {
        _time = time ?? TimeProvider.System;
        MaxWidth = maxWidth;
    }

    /// <summary>Max display width in "half-width units" (CJK = 2, others = 1).</summary>
    public int MaxWidth { get; set; }
    public TimeSpan CommitAfter { get; set; } = TimeSpan.FromSeconds(1.5);
    public TimeSpan ClearAfter { get; set; } = TimeSpan.FromSeconds(5);

    public string Previous { get; private set; } = "";
    public string Current => _current.ToString();

    public string DisplayText
    {
        get
        {
            var cur = Current.Trim();
            if (Previous.Length == 0) return cur;
            return cur.Length == 0 ? Previous : Previous + "\n" + cur;
        }
    }

    public bool Append(string fragment)
    {
        if (string.IsNullOrEmpty(fragment)) return false;
        if (_current.Length == 0) fragment = fragment.TrimStart();
        if (fragment.Length == 0) return false;
        _current.Append(fragment);
        _lastUpdate = _time.GetUtcNow();
        SplitCompleted();
        return true;
    }

    public bool Commit()
    {
        var text = _current.ToString().Trim();
        if (text.Length == 0) return false;
        Previous = text;
        _current.Clear();
        return true;
    }

    public void Clear()
    {
        Previous = "";
        _current.Clear();
    }

    public bool Tick()
    {
        if (_lastUpdate == default) return false;
        var idle = _time.GetUtcNow() - _lastUpdate;
        if (idle >= ClearAfter && (Previous.Length > 0 || _current.Length > 0))
        {
            Clear();
            return true;
        }
        if (idle >= CommitAfter && _current.Length > 0)
            return Commit();
        return false;
    }

    private void SplitCompleted()
    {
        while (true)
        {
            var s = _current.ToString();
            int cut = FindSentenceEnd(s);
            if (cut < 0) cut = FindWidthBreak(s, MaxWidth);
            if (cut <= 0 || cut >= s.Length) return;
            var head = s[..cut].Trim();
            var tail = s[cut..].TrimStart();
            if (head.Length > 0) Previous = head;
            _current.Clear().Append(tail);
        }
    }

    internal static bool IsCjk(char c) =>
        c is >= '\u1100' and <= '\u115F' or >= '\u2E80' and <= '\uA4CF' or >= '\uAC00' and <= '\uD7A3'
            or >= '\uF900' and <= '\uFAFF' or >= '\uFE30' and <= '\uFE4F' or >= '\uFF00' and <= '\uFF60' or >= '\uFFE0' and <= '\uFFE6';

    internal static bool IsCjkTerminator(char c) => c is '。' or '！' or '？' or '…' or '｡';
    internal static bool IsLatinTerminator(char c) => c is '.' or '!' or '?';

    /// <summary>Index just after the first sentence terminator that is followed by more text, or -1.</summary>
    internal static int FindSentenceEnd(string s)
    {
        for (int i = 0; i < s.Length - 1; i++)
        {
            char c = s[i];
            if (IsCjkTerminator(c))
            {
                int j = SkipClosers(s, i + 1);
                if (j < s.Length) return j;
            }
            else if (IsLatinTerminator(c))
            {
                int j = SkipClosers(s, i + 1);
                if (j < s.Length && char.IsWhiteSpace(s[j])) return j;
            }
        }
        return -1;
    }

    internal static bool EndsWithSentenceTerminator(string s)
    {
        s = s.TrimEnd();
        if (s.Length == 0) return false;
        int i = s.Length - 1;
        while (i > 0 && s[i] is '"' or '\'' or ')' or '”' or '’' or '」' or '』' or '）') i--;
        return IsCjkTerminator(s[i]) || IsLatinTerminator(s[i]);
    }

    private static int SkipClosers(string s, int i)
    {
        while (i < s.Length && s[i] is '"' or '\'' or ')' or '”' or '’' or '」' or '』' or '）' or '…' or '.' or '!' or '?' or '！' or '？')
            i++;
        return i;
    }

    internal static int FindWidthBreak(string s, int maxWidth)
    {
        int width = 0;
        int lastSoft = -1;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            width += IsCjk(c) ? 2 : 1;
            if (c == ' ' || c is '，' or '、' or '；' or ',' or ';') lastSoft = i + 1;
            if (width > maxWidth)
            {
                if (lastSoft > maxWidth / 3) return lastSoft;
                return i;
            }
        }
        return -1;
    }
}

/// <summary>
/// Maintains a rolling conversation history (<see cref="Entries"/>) where completed
/// sentences stay on screen above and new sentences append at the bottom.
/// Call from the UI thread.
/// </summary>
public sealed class SubtitleAssembler
{
    private readonly TimeProvider _time;
    private readonly StringBuilder _origBuf = new();
    private readonly StringBuilder _transBuf = new();
    private SubtitleEntry? _active;
    private DateTimeOffset _lastUpdate;

    public SubtitleAssembler(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
        Original = new SubtitleTrack(_time, maxWidth: 90);
        Translated = new SubtitleTrack(_time, maxWidth: 60);
    }

    public SubtitleTrack Original { get; }
    public SubtitleTrack Translated { get; }

    /// <summary>Scrolling conversation history, oldest at top, newest/active at bottom.</summary>
    public ObservableCollection<SubtitleEntry> Entries { get; } = [];

    public int MaxEntries { get; set; } = 200;

    /// <summary>Pause duration after which the active utterance is committed as a finished history item.</summary>
    public TimeSpan CommitAfter { get; set; } = TimeSpan.FromSeconds(1.8);

    /// <summary>Max width of one utterance before splitting into a new history item.</summary>
    public int MaxUtteranceWidth { get; set; } = 120;

    public event Action? Changed;

    public void AppendOriginal(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        Original.Append(text);
        if (_origBuf.Length == 0) text = text.TrimStart();
        if (text.Length == 0) return;

        // If the active entry already has both finished sentence punctuation and a pause/translation,
        // and new original speech starts after a sentence end, split into a new entry.
        if (_active is not null && _origBuf.Length > 0 && _transBuf.Length > 0 &&
            SubtitleTrack.EndsWithSentenceTerminator(_origBuf.ToString()) &&
            SubtitleTrack.EndsWithSentenceTerminator(_transBuf.ToString()))
        {
            SealActive();
        }

        _origBuf.Append(text);
        _lastUpdate = _time.GetUtcNow();
        EnsureActive().Original = _origBuf.ToString().Trim();
        SplitIfOverlong();
        Changed?.Invoke();
    }

    public void AppendTranslated(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        Translated.Append(text);
        if (_transBuf.Length == 0) text = text.TrimStart();
        if (text.Length == 0) return;

        _transBuf.Append(text);
        _lastUpdate = _time.GetUtcNow();
        EnsureActive().Translated = _transBuf.ToString().Trim();
        SplitIfOverlong();
        Changed?.Invoke();
    }

    public void TurnComplete()
    {
        bool a = Original.Commit();
        bool b = Translated.Commit();
        bool c = SealActive();
        if (a || b || c) Changed?.Invoke();
    }

    public void Tick()
    {
        bool a = Original.Tick();
        bool b = Translated.Tick();
        bool c = false;
        if (_active is not null && _lastUpdate != default && (_time.GetUtcNow() - _lastUpdate) >= CommitAfter)
            c = SealActive();
        if (a || b || c) Changed?.Invoke();
    }

    public void Clear()
    {
        Original.Clear();
        Translated.Clear();
        _origBuf.Clear();
        _transBuf.Clear();
        _active = null;
        Entries.Clear();
        Changed?.Invoke();
    }

    private SubtitleEntry EnsureActive()
    {
        if (_active is not null) return _active;
        _active = new SubtitleEntry(_time.GetUtcNow());
        Entries.Add(_active);
        TrimHistory();
        return _active;
    }

    private bool SealActive()
    {
        if (_active is null) return false;
        _active.Original = _origBuf.ToString().Trim();
        _active.Translated = _transBuf.ToString().Trim();
        _active.IsActive = false;
        _active = null;
        _origBuf.Clear();
        _transBuf.Clear();
        return true;
    }

    private void SplitIfOverlong()
    {
        // When continuous speech runs very long without a pause, split at a sentence boundary
        // once both buffers have completed at least one sentence, or when either exceeds MaxUtteranceWidth.
        var orig = _origBuf.ToString();
        var trans = _transBuf.ToString();
        int origCut = SubtitleTrack.FindSentenceEnd(orig);
        int transCut = SubtitleTrack.FindSentenceEnd(trans);

        if (origCut > 0 && transCut > 0 && _active is not null)
        {
            _active.Original = orig[..origCut].Trim();
            _active.Translated = trans[..transCut].Trim();
            _active.IsActive = false;
            _active = null;

            var origTail = orig[origCut..].TrimStart();
            var transTail = trans[transCut..].TrimStart();
            _origBuf.Clear().Append(origTail);
            _transBuf.Clear().Append(transTail);
            if (_origBuf.Length > 0 || _transBuf.Length > 0)
            {
                var next = EnsureActive();
                next.Original = _origBuf.ToString().Trim();
                next.Translated = _transBuf.ToString().Trim();
            }
            return;
        }

        // Fallback: if only one track is active (e.g. translation-only or very long clause), split by width.
        int tBreak = SubtitleTrack.FindWidthBreak(trans, MaxUtteranceWidth);
        int oBreak = SubtitleTrack.FindWidthBreak(orig, MaxUtteranceWidth + 40);
        if ((tBreak > 0 || oBreak > 0) && _active is not null)
        {
            if (tBreak > 0 && oBreak <= 0 && orig.Length > 0) oBreak = orig.Length;
            if (oBreak > 0 && tBreak <= 0 && trans.Length > 0) tBreak = trans.Length;

            _active.Original = (oBreak > 0 ? orig[..oBreak] : orig).Trim();
            _active.Translated = (tBreak > 0 ? trans[..tBreak] : trans).Trim();
            _active.IsActive = false;
            _active = null;

            _origBuf.Clear().Append(oBreak > 0 && oBreak < orig.Length ? orig[oBreak..].TrimStart() : "");
            _transBuf.Clear().Append(tBreak > 0 && tBreak < trans.Length ? trans[tBreak..].TrimStart() : "");
            if (_origBuf.Length > 0 || _transBuf.Length > 0)
            {
                var next = EnsureActive();
                next.Original = _origBuf.ToString().Trim();
                next.Translated = _transBuf.ToString().Trim();
            }
        }
    }

    private void TrimHistory()
    {
        int max = Math.Max(10, MaxEntries);
        while (Entries.Count > max)
            Entries.RemoveAt(0);
    }
}
