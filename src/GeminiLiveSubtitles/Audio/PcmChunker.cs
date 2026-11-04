using System.Buffers;
using System.Buffers.Binary;

namespace GeminiLiveSubtitles.Audio;

/// <summary>
/// Accumulates PCM16 bytes into fixed-size chunks (default 100 ms @ 16 kHz) and
/// suppresses long runs of digital silence so we don't stream (and pay for) nothing.
/// A short tail of silence is still sent so the model can finalize the last sentence.
/// </summary>
public sealed class PcmChunker
{
    private readonly int _chunkBytes;
    private readonly int _maxSilentChunks;
    private readonly short _silenceThreshold;
    private readonly byte[] _buffer;
    private int _count;
    private int _silentRun;

    public PcmChunker(int chunkBytes = 3200, int maxSilentChunks = 20, short silenceThreshold = 8)
    {
        _chunkBytes = chunkBytes;
        _maxSilentChunks = maxSilentChunks;
        _silenceThreshold = silenceThreshold;
        _buffer = new byte[chunkBytes];
    }

    public void Push(ReadOnlySpan<byte> pcm, Action<byte[]> onChunk)
    {
        while (!pcm.IsEmpty)
        {
            int n = Math.Min(_chunkBytes - _count, pcm.Length);
            pcm[..n].CopyTo(_buffer.AsSpan(_count));
            _count += n;
            pcm = pcm[n..];
            if (_count == _chunkBytes)
            {
                _count = 0;
                if (IsSilent(_buffer))
                {
                    if (++_silentRun > _maxSilentChunks) continue;
                }
                else
                {
                    _silentRun = 0;
                }
                onChunk(_buffer.ToArray());
            }
        }
    }

    private bool IsSilent(ReadOnlySpan<byte> pcm)
    {
        for (int i = 0; i + 1 < pcm.Length; i += 2)
        {
            short s = BinaryPrimitives.ReadInt16LittleEndian(pcm[i..]);
            if (s > _silenceThreshold || s < -_silenceThreshold) return false;
        }
        return true;
    }
}
