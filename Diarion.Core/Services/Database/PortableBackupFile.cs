using System;
using System.IO;
using System.Text;

namespace Diarion.Services.Database;

/// <summary>
/// The on-disk envelope for a portable backup: a small plaintext header carrying the KDF parameters,
/// followed by a LiteDB file encrypted with the key those parameters derive.
/// <para>
/// The header has to be readable without the passphrase — that is what makes the backup portable at
/// all. It reveals nothing: a salt and an iteration count are public inputs by design.
/// </para>
/// <para>
/// Layout: magic (8 bytes ASCII) · iterations (int32 LE) · salt length (int32 LE) · salt · payload.
/// The magic doubles as the format version, so a future change gets a new tag instead of a flag.
/// </para>
/// </summary>
public static class PortableBackupFile
{
    public const string FileExtension = ".diarionbackup";

    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("DIARION1");

    /// <summary>Bytes needed before the salt can be sized; also the smallest possible valid header.</summary>
    private const int FixedHeaderSize = 16;

    public sealed record Header(int Iterations, byte[] Salt, long PayloadOffset);

    public static void Write(Stream destination, byte[] salt, int iterations, Stream payload)
    {
        destination.Write(Magic, 0, Magic.Length);
        WriteInt32(destination, iterations);
        WriteInt32(destination, salt.Length);
        destination.Write(salt, 0, salt.Length);
        payload.CopyTo(destination);
    }

    /// <summary>
    /// Reads the header, or returns null when this is not a portable backup at all — which is the
    /// normal case for a legacy device-key <c>.db</c> backup and must not be treated as corruption.
    /// </summary>
    public static Header? TryReadHeader(Stream source)
    {
        try
        {
            if (!source.CanSeek || source.Length < FixedHeaderSize)
            {
                return null;
            }

            source.Position = 0;

            var magic = ReadExactly(source, Magic.Length);
            for (var i = 0; i < Magic.Length; i++)
            {
                if (magic[i] != Magic[i])
                {
                    return null;
                }
            }

            var iterations = ReadInt32(source);
            var saltLength = ReadInt32(source);

            if (iterations is < 1 or > Helpers.BackupKeyDeriver.MaxIterations)
            {
                return null;
            }

            // Bound the salt against the file itself: a corrupt length must not allocate wildly.
            if (saltLength is < 1 or > 1024 || FixedHeaderSize + saltLength > source.Length)
            {
                return null;
            }

            var salt = ReadExactly(source, saltLength);
            return new Header(iterations, salt, FixedHeaderSize + saltLength);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Copies the encrypted database out of the envelope into a standalone file.</summary>
    public static void ExtractPayload(Stream source, Header header, Stream destination)
    {
        source.Position = header.PayloadOffset;
        source.CopyTo(destination);
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static int ReadInt32(Stream stream)
        => System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(ReadExactly(stream, 4));

    private static byte[] ReadExactly(Stream stream, int count)
    {
        var buffer = new byte[count];
        var read = 0;
        while (read < count)
        {
            var chunk = stream.Read(buffer, read, count - read);
            if (chunk <= 0)
            {
                throw new EndOfStreamException();
            }
            read += chunk;
        }
        return buffer;
    }
}
