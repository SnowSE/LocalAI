using System.Buffers.Binary;

namespace LocalAI.Audio;

/// <summary>Mono audio as floats between -1 and 1.</summary>
public sealed record AudioData(float[] Samples, int SampleRate)
{
    public TimeSpan Duration => TimeSpan.FromSeconds((double)Samples.Length / SampleRate);

    /// <summary>
    /// Decode a WAV (16-bit, 24-bit, 32-bit or float PCM, any channel count) or MP3 file, mixing
    /// down to mono.
    /// </summary>
    /// <exception cref="FormatException">Neither a WAV nor an MP3 this decoder understands.</exception>
    public static AudioData Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WAVE"u8))
            return DecodeWav(bytes);
        if (LooksLikeMp3(bytes))
            return DecodeMp3(bytes.ToArray());
        throw new FormatException("Unsupported audio: expected a WAV or MP3 file.");
    }

    /// <inheritdoc cref="Decode(ReadOnlySpan{byte})"/>
    public static async Task<AudioData> DecodeAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return Decode(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
    }

    /// <summary>This audio at <paramref name="sampleRate"/>, by linear interpolation (plenty for speech models).</summary>
    public AudioData Resample(int sampleRate)
    {
        if (sampleRate == SampleRate || Samples.Length == 0)
            return this;
        var ratio = (double)SampleRate / sampleRate;
        var length = (int)(Samples.Length / ratio);
        var output = new float[length];
        for (var i = 0; i < length; i++)
        {
            var position = i * ratio;
            var index = (int)position;
            var fraction = (float)(position - index);
            var a = Samples[Math.Min(index, Samples.Length - 1)];
            var b = Samples[Math.Min(index + 1, Samples.Length - 1)];
            output[i] = a + (b - a) * fraction;
        }
        return new AudioData(output, sampleRate);
    }

    /// <summary>This audio as a 16-bit mono WAV file.</summary>
    public byte[] ToWav()
    {
        var data = Samples.Length * 2;
        var wav = new byte[44 + data];
        var span = wav.AsSpan();
        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 36 + data);
        "WAVE"u8.CopyTo(span[8..]);
        "fmt "u8.CopyTo(span[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1); // PCM
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], 1); // Mono
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], SampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], 2);
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], 16);
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], data);
        for (var i = 0; i < Samples.Length; i++)
            BinaryPrimitives.WriteInt16LittleEndian(span[(44 + i * 2)..], (short)Math.Clamp(Samples[i] * 32767f, short.MinValue, short.MaxValue));
        return wav;
    }

    private static AudioData DecodeWav(ReadOnlySpan<byte> wav)
    {
        int format = 0, channels = 0, sampleRate = 0, bits = 0;
        var position = 12;
        while (position + 8 <= wav.Length)
        {
            var id = wav.Slice(position, 4);
            var size = BinaryPrimitives.ReadInt32LittleEndian(wav.Slice(position + 4, 4));
            // Streams written before their length is known put 0 or -1 here; read to the end then.
            var available = wav.Length - position - 8;
            var length = size <= 0 || size > available ? available : size;
            var body = wav.Slice(position + 8, length);
            if (id.SequenceEqual("fmt "u8))
            {
                format = BinaryPrimitives.ReadUInt16LittleEndian(body);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(body[2..]);
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(body[4..]);
                bits = BinaryPrimitives.ReadUInt16LittleEndian(body[14..]);
                if (format == 0xFFFE && body.Length >= 26)
                    format = BinaryPrimitives.ReadUInt16LittleEndian(body[24..]); // WAVE_FORMAT_EXTENSIBLE
            }
            else if (id.SequenceEqual("data"u8))
            {
                if (channels == 0)
                    throw new FormatException("WAV data comes before its format.");
                return new AudioData(DecodePcm(body, format, channels, bits), sampleRate);
            }
            position += 8 + length + (length & 1);
        }
        throw new FormatException("The WAV file has no audio data.");
    }

    private static float[] DecodePcm(ReadOnlySpan<byte> data, int format, int channels, int bits)
    {
        var bytesPerSample = bits / 8;
        if (bytesPerSample == 0)
            throw new FormatException($"Unsupported WAV encoding ({bits}-bit).");
        var frames = data.Length / (bytesPerSample * channels);
        var samples = new float[frames];
        for (var frame = 0; frame < frames; frame++)
        {
            float sum = 0;
            for (var channel = 0; channel < channels; channel++)
            {
                var at = data.Slice((frame * channels + channel) * bytesPerSample, bytesPerSample);
                sum += (format, bits) switch
                {
                    (1, 8) => (at[0] - 128) / 128f,
                    (1, 16) => BinaryPrimitives.ReadInt16LittleEndian(at) / 32768f,
                    (1, 24) => ((at[2] << 24) | (at[1] << 16) | (at[0] << 8)) / 2147483648f,
                    (1, 32) => BinaryPrimitives.ReadInt32LittleEndian(at) / 2147483648f,
                    (3, 32) => BinaryPrimitives.ReadSingleLittleEndian(at),
                    (3, 64) => (float)BinaryPrimitives.ReadDoubleLittleEndian(at),
                    _ => throw new FormatException($"Unsupported WAV encoding (format {format}, {bits}-bit)."),
                };
            }
            samples[frame] = sum / channels;
        }
        return samples;
    }

    private static bool LooksLikeMp3(ReadOnlySpan<byte> bytes) =>
        (bytes.Length >= 3 && bytes[..3].SequenceEqual("ID3"u8)) ||
        (bytes.Length >= 2 && bytes[0] == 0xFF && (bytes[1] & 0xE0) == 0xE0);

    private static AudioData DecodeMp3(byte[] bytes)
    {
        using var reader = new NLayer.MpegFile(new MemoryStream(bytes));
        var channels = reader.Channels;
        var interleaved = new List<float>();
        var buffer = new float[8192];
        int read;
        while ((read = reader.ReadSamples(buffer, 0, buffer.Length)) > 0)
            interleaved.AddRange(buffer.AsSpan(0, read));
        var frames = interleaved.Count / channels;
        var mono = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            float sum = 0;
            for (var c = 0; c < channels; c++)
                sum += interleaved[i * channels + c];
            mono[i] = sum / channels;
        }
        return new AudioData(mono, reader.SampleRate);
    }
}
