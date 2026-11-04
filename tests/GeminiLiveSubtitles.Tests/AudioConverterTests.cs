using System.Buffers;
using System.Buffers.Binary;
using GeminiLiveSubtitles.Audio;

namespace GeminiLiveSubtitles.Tests;

public class AudioConverterTests
{
    private static byte[] SineFloatStereo(int rate, double freq, double seconds, float amp = 0.5f)
    {
        int frames = (int)(rate * seconds);
        var bytes = new byte[frames * 2 * 4];
        for (int i = 0; i < frames; i++)
        {
            float v = amp * (float)Math.Sin(2 * Math.PI * freq * i / rate);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 8), v);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 8 + 4), v);
        }
        return bytes;
    }

    private static short[] ToShorts(ReadOnlySpan<byte> pcm)
    {
        var s = new short[pcm.Length / 2];
        for (int i = 0; i < s.Length; i++) s[i] = BinaryPrimitives.ReadInt16LittleEndian(pcm[(i * 2)..]);
        return s;
    }

    private static int ZeroCrossings(short[] s, int skip)
    {
        int n = 0;
        for (int i = skip + 1; i < s.Length; i++)
            if ((s[i - 1] < 0 && s[i] >= 0) || (s[i - 1] >= 0 && s[i] < 0)) n++;
        return n;
    }

    [Theory]
    [InlineData(48000)]
    [InlineData(44100)]
    [InlineData(16000)]
    public void Converts_StereoFloat_To_16kMonoPcm16_PreservingFrequency(int rate)
    {
        var conv = new AudioConverter(rate, 2, SampleFormat.Float32);
        var output = new ArrayBufferWriter<byte>();
        conv.Convert(SineFloatStereo(rate, 1000, 1.0), output);
        var samples = ToShorts(output.WrittenSpan);

        Assert.InRange(samples.Length, 15990, 16000);
        // 1 kHz -> ~2000 zero crossings per second
        int zc = ZeroCrossings(samples, 200);
        Assert.InRange(zc, (int)(2000 * (samples.Length - 200) / 16000.0) - 10, (int)(2000 * (samples.Length - 200) / 16000.0) + 10);
        // amplitude ~0.5 * 32767 (allow passband ripple)
        int peak = samples.Skip(200).Max(s => Math.Abs((int)s));
        Assert.InRange(peak, 15000, 17500);
    }

    [Fact]
    public void ChunkBoundaries_AreSeamless_EvenWhenSplittingFrames()
    {
        var input = SineFloatStereo(48000, 440, 0.5);
        var whole = new ArrayBufferWriter<byte>();
        new AudioConverter(48000, 2, SampleFormat.Float32).Convert(input, whole);

        var pieces = new ArrayBufferWriter<byte>();
        var conv = new AudioConverter(48000, 2, SampleFormat.Float32);
        var rnd = new Random(42);
        int pos = 0;
        while (pos < input.Length)
        {
            int n = Math.Min(rnd.Next(1, 2000), input.Length - pos); // deliberately not frame-aligned
            conv.Convert(input.AsSpan(pos, n), pieces);
            pos += n;
        }

        Assert.Equal(whole.WrittenSpan.ToArray(), pieces.WrittenSpan.ToArray());
    }

    [Fact]
    public void HighFrequencies_AreAttenuated_BeforeDownsampling()
    {
        var conv = new AudioConverter(48000, 2, SampleFormat.Float32);
        var output = new ArrayBufferWriter<byte>();
        conv.Convert(SineFloatStereo(48000, 12000, 0.5), output); // above 8 kHz Nyquist -> would alias
        var samples = ToShorts(output.WrittenSpan);
        int peak = samples.Skip(100).Max(s => Math.Abs((int)s));
        Assert.True(peak < 800, $"Aliased energy too high: {peak}");
    }

    [Fact]
    public void Pcm16_Mono_Passthrough_At16k()
    {
        var conv = new AudioConverter(16000, 1, SampleFormat.Pcm16);
        var input = new byte[3200];
        for (int i = 0; i < 1600; i++) BinaryPrimitives.WriteInt16LittleEndian(input.AsSpan(i * 2), (short)(i * 10));
        var output = new ArrayBufferWriter<byte>();
        conv.Convert(input, output);
        var s = ToShorts(output.WrittenSpan);
        Assert.Equal(1599, s.Length); // last sample held back for interpolation
        for (int i = 0; i < s.Length; i++) Assert.InRange(s[i], i * 10 - 1, i * 10 + 1);
    }

    [Fact]
    public void Pcm24_IsDecodedWithSign()
    {
        var conv = new AudioConverter(16000, 1, SampleFormat.Pcm24);
        // -0.5 full scale in 24-bit = 0xC00000
        var input = new byte[] { 0x00, 0x00, 0xC0, 0x00, 0x00, 0xC0, 0x00, 0x00, 0xC0 };
        var output = new ArrayBufferWriter<byte>();
        conv.Convert(input, output);
        var s = ToShorts(output.WrittenSpan);
        Assert.All(s, v => Assert.InRange(v, -16400, -16300));
    }

    [Fact]
    public void Chunker_Emits100msChunks_AndSuppressesLongSilence()
    {
        var chunker = new PcmChunker(chunkBytes: 3200, maxSilentChunks: 3);
        var chunks = new List<byte[]>();
        chunker.Push(new byte[3200 * 10], chunks.Add); // 10 silent chunks
        Assert.Equal(3, chunks.Count);

        var loud = new byte[3200];
        BinaryPrimitives.WriteInt16LittleEndian(loud.AsSpan(100), 5000);
        chunker.Push(loud.AsSpan(0, 1000), chunks.Add);
        Assert.Equal(3, chunks.Count);
        chunker.Push(loud.AsSpan(1000), chunks.Add);
        Assert.Equal(4, chunks.Count);
        Assert.All(chunks, c => Assert.Equal(3200, c.Length));
    }
}
