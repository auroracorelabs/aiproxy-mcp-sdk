using System.Security.Cryptography;
using System.Text;

namespace AiProxy.Mcp.Sdk.Internal;

/// <summary>
/// PEM / DER encoding of ECDSA P-256 keys on every target (ImportFromPem and the SPKI / PKCS#8
/// import-export APIs are .NET Core 3+). Encodings are byte-identical to the .NET ones:
/// SPKI "PUBLIC KEY", PKCS#8 "PRIVATE KEY" (with public key); SEC1 "EC PRIVATE KEY" is also read.
/// </summary>
internal static class P256KeyEncoding
{
    private const int CoordinateSize = 32;
    private const string PublicKeyLabel = "PUBLIC KEY";
    private const string Pkcs8Label = "PRIVATE KEY";
    private const string Sec1Label = "EC PRIVATE KEY";

    // 1.2.840.10045.2.1 (id-ecPublicKey) and 1.2.840.10045.3.1.7 (prime256v1 / P-256).
    private static readonly byte[] EcPublicKeyOid = [0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x02, 0x01];
    private static readonly byte[] P256Oid = [0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x03, 0x01, 0x07];

    public static ECDsa ImportPublicKeyPem(string pem)
    {
        var der = DecodePem(pem, PublicKeyLabel)
            ?? throw new CryptographicException($"Expected a PEM '{PublicKeyLabel}' block.");
        return ImportSubjectPublicKeyInfo(der);
    }

    public static ECDsa ImportSubjectPublicKeyInfo(byte[] der)
    {
        var outer = ReadSingleSequence(der);
        ReadAlgorithm(outer.ReadSequence());
        var point = outer.ReadBitString();
        outer.ExpectEnd();
        var (x, y) = ParseUncompressedPoint(point);
        return Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = x, Y = y } });
    }

    /// <summary>Reads PKCS#8 (<c>PRIVATE KEY</c>) or SEC1 (<c>EC PRIVATE KEY</c>); the public key must be embedded.</summary>
    public static ECDsa ImportPrivateKeyPem(string pem)
    {
        var pkcs8 = DecodePem(pem, Pkcs8Label);
        if (pkcs8 is not null)
        {
            var outer = ReadSingleSequence(pkcs8);
            if (!IsZero(outer.ReadInteger()))
                throw new CryptographicException("Unsupported PKCS#8 version.");
            ReadAlgorithm(outer.ReadSequence());
            return ImportSec1(outer.ReadOctetString());
        }

        var sec1 = DecodePem(pem, Sec1Label)
            ?? throw new CryptographicException($"Expected a PEM '{Pkcs8Label}' or '{Sec1Label}' block.");
        return ImportSec1(sec1);
    }

    public static byte[] ExportSubjectPublicKeyInfo(ECDsa key)
    {
        var p = ExportP256(key, includePrivate: false);
        var bytes = new List<byte> { 0x30, 0x59 };
        bytes.AddRange(AlgorithmIdentifier());
        bytes.AddRange(new byte[] { 0x03, 0x42, 0x00, 0x04 });
        bytes.AddRange(p.Q.X!);
        bytes.AddRange(p.Q.Y!);
        return bytes.ToArray();
    }

    public static string ExportPublicKeyPem(ECDsa key) => EncodePem(PublicKeyLabel, ExportSubjectPublicKeyInfo(key));

    public static string ExportPkcs8PrivateKeyPem(ECDsa key)
    {
        var p = ExportP256(key, includePrivate: true);
        var bytes = new List<byte> { 0x30, 0x81, 0x87, 0x02, 0x01, 0x00 };
        bytes.AddRange(AlgorithmIdentifier());
        bytes.AddRange(new byte[] { 0x04, 0x6D, 0x30, 0x6B, 0x02, 0x01, 0x01, 0x04, 0x20 });
        bytes.AddRange(p.D!);
        bytes.AddRange(new byte[] { 0xA1, 0x44, 0x03, 0x42, 0x00, 0x04 });
        bytes.AddRange(p.Q.X!);
        bytes.AddRange(p.Q.Y!);
        return EncodePem(Pkcs8Label, bytes.ToArray());
    }

    /// <summary>True when <paramref name="key"/> is on the named curve P-256.</summary>
    public static bool IsP256(ECDsa key)
    {
        if (key.KeySize != 256)
            return false;
        var curve = key.ExportParameters(includePrivateParameters: false).Curve;
        return curve.IsNamed
            && (string.Equals(curve.Oid.Value, ECCurve.NamedCurves.nistP256.Oid.Value, StringComparison.Ordinal)
                || string.Equals(curve.Oid.FriendlyName, "nistP256", StringComparison.OrdinalIgnoreCase)
                || string.Equals(curve.Oid.FriendlyName, "ECDSA_P256", StringComparison.OrdinalIgnoreCase));
    }

    private static ECDsa ImportSec1(byte[] der)
    {
        var ecPrivateKey = ReadSingleSequence(der);
        var version = ecPrivateKey.ReadInteger();
        if (version.Length != 1 || version[0] != 1)
            throw new CryptographicException("Unsupported EC private key version.");
        var d = LeftPad(ecPrivateKey.ReadOctetString());

        byte[]? point = null;
        while (ecPrivateKey.HasMore)
        {
            var tag = ecPrivateKey.PeekTag();
            var field = ecPrivateKey.ReadElement(tag);
            if (tag == 0xA0)
            {
                if (!Same(field.ReadObjectIdentifier(), P256Oid))
                    throw new CryptographicException("The key must be an ECDSA P-256 key.");
            }
            else if (tag == 0xA1)
            {
                point = field.ReadBitString();
            }
        }

        if (point is null)
            throw new CryptographicException("The EC private key does not embed its public key.");
        var (x, y) = ParseUncompressedPoint(point);
        return Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = d,
            Q = new ECPoint { X = x, Y = y },
        });
    }

    /// <summary>The whole buffer must be exactly one SEQUENCE: trailing bytes are rejected.</summary>
    private static DerReader ReadSingleSequence(byte[] der)
    {
        var top = new DerReader(der);
        var sequence = top.ReadSequence();
        top.ExpectEnd();
        return sequence;
    }

    private static void ReadAlgorithm(DerReader algorithm)
    {
        if (!Same(algorithm.ReadObjectIdentifier(), EcPublicKeyOid))
            throw new CryptographicException("The key is not an EC key.");
        if (!Same(algorithm.ReadObjectIdentifier(), P256Oid))
            throw new CryptographicException("The key must be an ECDSA P-256 key.");
        algorithm.ExpectEnd();
    }

    private static (byte[] X, byte[] Y) ParseUncompressedPoint(byte[] point)
    {
        if (point.Length != 1 + (2 * CoordinateSize) || point[0] != 0x04)
            throw new CryptographicException("The public key must be an uncompressed P-256 point.");
        var x = new byte[CoordinateSize];
        var y = new byte[CoordinateSize];
        Buffer.BlockCopy(point, 1, x, 0, CoordinateSize);
        Buffer.BlockCopy(point, 1 + CoordinateSize, y, 0, CoordinateSize);
        return (x, y);
    }

    private static ECDsa Create(ECParameters parameters)
    {
        var key = ECDsa.Create();
        try
        {
            key.ImportParameters(parameters);
            return key;
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    private static ECParameters ExportP256(ECDsa key, bool includePrivate)
    {
        Guard.NotNull(key, nameof(key));
        if (!IsP256(key))
            throw new CryptographicException("The key must be an ECDSA P-256 key.");
        var p = key.ExportParameters(includePrivate);
        p.Q.X = LeftPad(p.Q.X!);
        p.Q.Y = LeftPad(p.Q.Y!);
        if (includePrivate)
            p.D = LeftPad(p.D ?? throw new CryptographicException("The key has no private part."));
        return p;
    }

    private static byte[] AlgorithmIdentifier()
    {
        var bytes = new List<byte> { 0x30, 0x13, 0x06, (byte)EcPublicKeyOid.Length };
        bytes.AddRange(EcPublicKeyOid);
        bytes.Add(0x06);
        bytes.Add((byte)P256Oid.Length);
        bytes.AddRange(P256Oid);
        return bytes.ToArray();
    }

    private static byte[] LeftPad(byte[] value)
    {
        var start = 0;
        while (value.Length - start > CoordinateSize && value[start] == 0)
            start++;
        if (value.Length - start > CoordinateSize)
            throw new CryptographicException("Invalid P-256 scalar or coordinate length.");
        var padded = new byte[CoordinateSize];
        Buffer.BlockCopy(value, start, padded, CoordinateSize - (value.Length - start), value.Length - start);
        return padded;
    }

    private static bool IsZero(byte[] integer) => integer.Length == 1 && integer[0] == 0;

    private static bool Same(byte[] a, byte[] b) => a.Length == b.Length && a.SequenceEqual(b);

    private static byte[]? DecodePem(string pem, string label)
    {
        Guard.NotNull(pem, nameof(pem));
        var begin = "-----BEGIN " + label + "-----";
        var end = "-----END " + label + "-----";
        var start = pem.IndexOf(begin, StringComparison.Ordinal);
        if (start < 0)
            return null;
        start += begin.Length;
        var stop = pem.IndexOf(end, start, StringComparison.Ordinal);
        if (stop < 0)
            throw new CryptographicException($"PEM '{label}' block is not terminated.");

        var body = new StringBuilder(stop - start);
        for (var i = start; i < stop; i++)
        {
            if (!char.IsWhiteSpace(pem[i]))
                body.Append(pem[i]);
        }

        try
        {
            return Convert.FromBase64String(body.ToString());
        }
        catch (FormatException)
        {
            throw new CryptographicException($"PEM '{label}' block is not valid base64.");
        }
    }

    private static string EncodePem(string label, byte[] der)
    {
        var base64 = Convert.ToBase64String(der);
        var sb = new StringBuilder();
        sb.Append("-----BEGIN ").Append(label).Append("-----\n");
        for (var i = 0; i < base64.Length; i += 64)
            sb.Append(base64, i, Math.Min(64, base64.Length - i)).Append('\n');
        sb.Append("-----END ").Append(label).Append("-----");
        return sb.ToString();
    }

    /// <summary>Strict reader for the handful of DER types used by EC key encodings.</summary>
    private sealed class DerReader
    {
        private readonly byte[] _data;
        private readonly int _end;
        private int _position;

        public DerReader(byte[] data)
            : this(data, 0, data.Length)
        {
        }

        private DerReader(byte[] data, int start, int end)
        {
            _data = data;
            _position = start;
            _end = end;
        }

        public bool HasMore => _position < _end;

        public byte PeekTag()
        {
            if (!HasMore)
                throw Malformed();
            return _data[_position];
        }

        public DerReader ReadSequence() => ReadElement(0x30);

        public byte[] ReadInteger() => ReadElement(0x02).Rest();

        public byte[] ReadOctetString() => ReadElement(0x04).Rest();

        public byte[] ReadObjectIdentifier() => ReadElement(0x06).Rest();

        /// <summary>Bit string content without the unused-bits byte (which must be 0).</summary>
        public byte[] ReadBitString()
        {
            var content = ReadElement(0x03).Rest();
            if (content.Length == 0 || content[0] != 0)
                throw Malformed();
            var bits = new byte[content.Length - 1];
            Buffer.BlockCopy(content, 1, bits, 0, bits.Length);
            return bits;
        }

        public DerReader ReadElement(byte expectedTag)
        {
            if (PeekTag() != expectedTag)
                throw Malformed();
            _position++;
            var length = ReadLength();
            if (length > _end - _position)
                throw Malformed();
            var element = new DerReader(_data, _position, _position + length);
            _position += length;
            return element;
        }

        public void ExpectEnd()
        {
            if (HasMore)
                throw Malformed();
        }

        private byte[] Rest()
        {
            var bytes = new byte[_end - _position];
            Buffer.BlockCopy(_data, _position, bytes, 0, bytes.Length);
            _position = _end;
            return bytes;
        }

        private int ReadLength()
        {
            if (!HasMore)
                throw Malformed();
            int first = _data[_position++];
            if (first < 0x80)
                return first;

            var count = first & 0x7F;
            if (count is 0 or > 3 || count > _end - _position)
                throw Malformed();
            var length = 0;
            for (var i = 0; i < count; i++)
                length = (length << 8) | _data[_position++];
            return length;
        }

        private static CryptographicException Malformed() => new("Malformed DER key encoding.");
    }
}
