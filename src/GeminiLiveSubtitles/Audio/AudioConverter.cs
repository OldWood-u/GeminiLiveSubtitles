using System.Buffers;
using System.Buffers.Binary;
using NAudio.Wave;

namespace GeminiLiveSubtitles.Audio;

public enum SampleFormat
{
    Float32,
    Pcm16,
    Pcm24,
    Pcm32,
}

/// <summary>
/// Streaming converter: interleaved device audio (any rate / channel count) →
/// mono, 16 kHz, 16-bit little-endian PCM as required by the Gemini Live API.
/// Down-mixes by averaging channels, applies a windowed-sinc anti-alias low-pass
/// when down-sampling, then resamples with linear interpolation. Keeps state between
/// calls so chunk boundaries are seamless.
/// </summary>
public sealed class AudioConverter
{
    private readonly int _channels;
    private readonly SampleFormat _format;
    private readonly int _bytesPerFrame;
    private readonly double _step;          // source samples per output sample
    private readonly float[] _taps;         // FIR coefficients (empty = no filtering)
    private readonly float[] _history;      // last (taps-1) mono samples for FIR
    private double _pos;                    // next output position relative to the current block
    private float _prev;                    // last filtered sample of the previous block (index -1)
    private float[] _mono = [];
    private float[] _filtered = [];
    private readonly byte[] _partial;       // leftover bytes of an incomplete frame
    private int _partialCount;

    public AudioConverter(int sampleRate, int channels, SampleFormat format, int targetRate = 16000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(sampleRate, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(channels, 0);
        SourceRate = sampleRate;
        TargetRate = targetRate;
        _channels = channels;
        _format = format;
        _bytesPerFrame = BytesPerSample(format) * channels;
        _partial = new byte[_bytesPerFrame];
        _step = (double)sampleRate / targetRate;
        _taps = sampleRate > targetRate ? DesignLowPass(0.45 * targetRate / sampleRate, 63) : [];
        _history = new float[Math.Max(0, _taps.Length - 1)];
    }

    public int SourceRate { get; }
    public int TargetRate { get; }

    /// <summary>KSDATAFORMAT_SUBTYPE_IEEE_FLOAT</summary>
    private static readonly Guid IeeeFloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");

    public static AudioConverter FromWaveFormat(WaveFormat wf, int targetRate = 16000)
    {
        var format = wf.Encoding switch
        {
            WaveFormatEncoding.IeeeFloat => SampleFormat.Float32,
            WaveFormatEncoding.Pcm => PcmFromBits(wf.BitsPerSample),
            WaveFormatEncoding.Extensible when wf is WaveFormatExtensible ext =>
                ext.SubFormat == IeeeFloatSubFormat ? SampleFormat.Float32 : PcmFromBits(wf.BitsPerSample),
            WaveFormatEncoding.Extensible => wf.BitsPerSample == 32 ? SampleFormat.Float32 : PcmFromBits(wf.BitsPerSample),
            _ => throw new NotSupportedException($"Unsupported audio encoding {wf.Encoding}"),
        };
        return new AudioConverter(wf.SampleRate, wf.Channels, format, targetRate);
    }

    private static SampleFormat PcmFromBits(int bits) => bits switch
    {
        16 => SampleFormat.Pcm16,
        24 => SampleFormat.Pcm24,
        32 => SampleFormat.Pcm32,
        _ => throw new NotSupportedException($"Unsupported PCM bit depth {bits}"),
    };

    private static int BytesPerSample(SampleFormat f) => f switch
    {
        SampleFormat.Float32 or SampleFormat.Pcm32 => 4,
        SampleFormat.Pcm24 => 3,
        _ => 2,
    };

    /// <summary>Converts a block of interleaved source bytes, appending PCM16 bytes to <paramref name="output"/>.</summary>
    /// <returns>Number of output bytes written.</returns>
    public int Convert(ReadOnlySpan<byte> input, IBufferWriter<byte> output)
    {
        // Re-assemble a frame split across calls.
        byte[]? joined = null;
        if (_partialCount > 0)
        {
            joined = new byte[_partialCount + input.Length];
            _partial.AsSpan(0, _partialCount).CopyTo(joined);
            input.CopyTo(joined.AsSpan(_partialCount));
            input = joined;
            _partialCount = 0;
        }

        int frames = input.Length / _bytesPerFrame;
        int rem = input.Length - frames * _bytesPerFrame;
        if (rem > 0)
        {
            input.Slice(frames * _bytesPerFrame).CopyTo(_partial);
            _partialCount = rem;
        }
        if (frames == 0) return 0;

        EnsureCapacity(ref _mono, frames);
        DownMix(input[..(frames * _bytesPerFrame)], _mono.AsSpan(0, frames));

        ReadOnlySpan<float> block;
        if (_taps.Length > 0)
        {
            EnsureCapacity(ref _filtered, frames);
            Filter(_mono.AsSpan(0, frames), _filtered.AsSpan(0, frames));
            block = _filtered.AsSpan(0, frames);
        }
        else
        {
            block = _mono.AsSpan(0, frames);
        }

        return Resample(block, output);
    }

    private void DownMix(ReadOnlySpan<byte> src, Span<float> dst)
    {
        int bps = BytesPerSample(_format);
        float inv = 1f / _channels;
        for (int f = 0; f < dst.Length; f++)
        {
            float sum = 0;
            int baseOff = f * _bytesPerFrame;
            for (int c = 0; c < _channels; c++)
            {
                var s = src.Slice(baseOff + c * bps, bps);
                sum += _format switch
                {
                    SampleFormat.Float32 => BinaryPrimitives.ReadSingleLittleEndian(s),
                    SampleFormat.Pcm16 => BinaryPrimitives.ReadInt16LittleEndian(s) / 32768f,
                    SampleFormat.Pcm24 => ((s[0] | (s[1] << 8) | ((sbyte)s[2] << 16))) / 8388608f,
                    SampleFormat.Pcm32 => BinaryPrimitives.ReadInt32LittleEndian(s) / 2147483648f,
                    _ => 0f,
                };
            }
            dst[f] = sum * inv;
        }
    }

    private void Filter(ReadOnlySpan<float> x, Span<float> y)
    {
        int h = _history.Length;
        int n = _taps.Length;
        for (int i = 0; i < x.Length; i++)
        {
            float acc = 0;
            // y[i] = sum_k taps[k] * x[i-k], with x[negative] taken from history.
            for (int k = 0; k < n; k++)
            {
                int idx = i - k;
                float v = idx >= 0 ? x[idx] : _history[h + idx];
                acc += _taps[k] * v;
            }
            y[i] = acc;
        }
        // Update history with the last h samples (combining old history if block is short).
        if (x.Length >= h)
        {
            x[^h..].CopyTo(_history);
        }
        else
        {
            Array.Copy(_history, x.Length, _history, 0, h - x.Length);
            x.CopyTo(_history.AsSpan(h - x.Length));
        }
    }

    private int Resample(ReadOnlySpan<float> y, IBufferWriter<byte> output)
    {
        int n = y.Length;
        int maxOut = (int)Math.Ceiling((n + 1) / _step) + 2;
        var dst = output.GetSpan(maxOut * 2);
        int written = 0;
        while (_pos < n - 1)
        {
            int i = (int)Math.Floor(_pos);
            double frac = _pos - i;
            float a = i < 0 ? _prev : y[i];
            float b = y[i + 1];
            float v = (float)(a + (b - a) * frac);
            int s = (int)MathF.Round(v * 32767f);
            s = Math.Clamp(s, short.MinValue, short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(dst.Slice(written * 2), (short)s);
            written++;
            _pos += _step;
        }
        _pos -= n;
        _prev = y[n - 1];
        output.Advance(written * 2);
        return written * 2;
    }

    private static void EnsureCapacity(ref float[] arr, int size)
    {
        if (arr.Length < size) arr = new float[Math.Max(size, arr.Length * 2)];
    }

    /// <summary>Windowed-sinc (Blackman) low-pass. <paramref name="cutoff"/> is normalized to the sample rate (0..0.5).</summary>
    internal static float[] DesignLowPass(double cutoff, int taps)
    {
        var h = new float[taps];
        int m = taps - 1;
        double sum = 0;
        for (int i = 0; i < taps; i++)
        {
            double x = i - m / 2.0;
            double sinc = x == 0 ? 2 * cutoff : Math.Sin(2 * Math.PI * cutoff * x) / (Math.PI * x);
            double w = 0.42 - 0.5 * Math.Cos(2 * Math.PI * i / m) + 0.08 * Math.Cos(4 * Math.PI * i / m);
            h[i] = (float)(sinc * w);
            sum += h[i];
        }
        for (int i = 0; i < taps; i++) h[i] = (float)(h[i] / sum);
        return h;
    }
}
