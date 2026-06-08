using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Arcanum.Core;

public class InvalidVaultException(string message) : Exception(message) { }
public class AuthenticationException(string message) : Exception(message) { }

public static class Crypto
{
    private static readonly byte[] Magic = "PVLT"u8.ToArray();
    private const ushort FormatVersion  = 1;
    private const byte   KdfArgon2Id    = 0x01;
    private const int    SaltLen        = 32;
    private const int    NonceLen       = 12;
    private const int    KeyLen         = 32;
    private const int    TagLen         = 16;
    private const int    HeaderSize     = 71; // 4+2+1+4+4+4+32+12+8
    private const int    ArgonMCost     = 65536;
    private const int    ArgonTCost     = 3;
    private const int    ArgonPCost     = 4;

    private static byte[] DeriveKey(string password, byte[] salt,
        int mCost = ArgonMCost, int tCost = ArgonTCost, int pCost = ArgonPCost)
    {
        byte[] pwBytes = Encoding.UTF8.GetBytes(password);
        try
        {
            using var argon2 = new Argon2id(pwBytes);
            argon2.Salt                = salt;
            argon2.MemorySize          = mCost;
            argon2.Iterations          = tCost;
            argon2.DegreeOfParallelism = pCost;
            byte[] masterKey = argon2.GetBytes(KeyLen);

            byte[] encKey = HKDF.DeriveKey(
                HashAlgorithmName.SHA256,
                masterKey,
                KeyLen,
                salt: null,
                info: "pvault-enc-v1"u8.ToArray());

            CryptographicOperations.ZeroMemory(masterKey);
            return encKey;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pwBytes);
        }
    }

    // Big-endian AAD: 4s H B I I I 32s 12s = 63 bytes (header sem payload_len)
    private static byte[] BuildAad(byte[] salt, byte[] nonce,
        int mCost, int tCost, int pCost,
        ushort version = FormatVersion, byte kdfId = KdfArgon2Id)
    {
        var aad = new byte[63];
        int p = 0;
        Magic.CopyTo(aad, p); p += 4;
        BinaryPrimitives.WriteUInt16BigEndian(aad.AsSpan(p), version); p += 2;
        aad[p++] = kdfId;
        BinaryPrimitives.WriteUInt32BigEndian(aad.AsSpan(p), (uint)mCost); p += 4;
        BinaryPrimitives.WriteUInt32BigEndian(aad.AsSpan(p), (uint)tCost); p += 4;
        BinaryPrimitives.WriteUInt32BigEndian(aad.AsSpan(p), (uint)pCost); p += 4;
        salt.CopyTo(aad, p); p += 32;
        nonce.CopyTo(aad, p);
        return aad;
    }

    public static byte[] EncryptVault(string plaintextJson, string password)
    {
        byte[] salt  = RandomNumberGenerator.GetBytes(SaltLen);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceLen);

        byte[] payload;
        using (var ms = new MemoryStream())
        {
            using (var zlib = new ZLibStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
                zlib.Write(Encoding.UTF8.GetBytes(plaintextJson));
            payload = ms.ToArray();
        }

        byte[] encKey = DeriveKey(password, salt);
        byte[] aad    = BuildAad(salt, nonce, ArgonMCost, ArgonTCost, ArgonPCost);

        byte[] ciphertext = new byte[payload.Length];
        byte[] tag        = new byte[TagLen];
        using (var aesGcm = new AesGcm(encKey, TagLen))
            aesGcm.Encrypt(nonce, payload, ciphertext, tag, aad);
        CryptographicOperations.ZeroMemory(encKey);

        // Header big-endian: 4s H B I I I 32s 12s Q
        ulong ctLen = (ulong)(ciphertext.Length + TagLen);
        var output  = new byte[HeaderSize + (int)ctLen];
        int pos = 0;
        Magic.CopyTo(output, pos); pos += 4;
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(pos), FormatVersion); pos += 2;
        output[pos++] = KdfArgon2Id;
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(pos), ArgonMCost); pos += 4;
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(pos), ArgonTCost); pos += 4;
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(pos), ArgonPCost); pos += 4;
        salt.CopyTo(output, pos); pos += 32;
        nonce.CopyTo(output, pos); pos += 12;
        BinaryPrimitives.WriteUInt64BigEndian(output.AsSpan(pos), ctLen); pos += 8;
        ciphertext.CopyTo(output, pos); pos += ciphertext.Length;
        tag.CopyTo(output, pos);
        return output;
    }

    public static string DecryptVault(byte[] vaultBytes, string password)
    {
        if (vaultBytes.Length < HeaderSize)
            throw new InvalidVaultException("Arquivo muito pequeno para ser um ARCANUM válido.");

        var span = vaultBytes.AsSpan();
        int pos = 0;

        if (!span.Slice(pos, 4).SequenceEqual(Magic))
            throw new InvalidVaultException("Magic bytes inválidos.");
        pos += 4;

        ushort version = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(pos)); pos += 2;
        if (version != FormatVersion)
            throw new InvalidVaultException($"Versão não suportada: {version}");

        byte kdfId = span[pos++];
        if (kdfId != KdfArgon2Id)
            throw new InvalidVaultException($"KDF não suportado: {kdfId:X2}");

        int mCost = (int)BinaryPrimitives.ReadUInt32BigEndian(span.Slice(pos)); pos += 4;
        int tCost = (int)BinaryPrimitives.ReadUInt32BigEndian(span.Slice(pos)); pos += 4;
        int pCost = (int)BinaryPrimitives.ReadUInt32BigEndian(span.Slice(pos)); pos += 4;

        byte[] salt  = span.Slice(pos, 32).ToArray(); pos += 32;
        byte[] nonce = span.Slice(pos, 12).ToArray(); pos += 12;

        ulong payloadLen = BinaryPrimitives.ReadUInt64BigEndian(span.Slice(pos)); pos += 8;

        if (payloadLen > int.MaxValue)
            throw new InvalidVaultException("Tamanho de payload inválido.");
        if (vaultBytes.Length < HeaderSize + (long)payloadLen)
            throw new InvalidVaultException("Tamanho do ciphertext inconsistente com o cabeçalho.");

        byte[] ctWithTag  = span.Slice(pos, (int)payloadLen).ToArray();
        byte[] ciphertext = ctWithTag[..^TagLen];
        byte[] tag        = ctWithTag[^TagLen..];

        byte[] aad    = BuildAad(salt, nonce, mCost, tCost, pCost, version, kdfId);
        byte[] encKey = DeriveKey(password, salt, mCost, tCost, pCost);
        byte[] payload = new byte[ciphertext.Length];
        try
        {
            using var aesGcm = new AesGcm(encKey, TagLen);
            aesGcm.Decrypt(nonce, ciphertext, tag, payload, aad);
        }
        catch (CryptographicException)
        {
            throw new AuthenticationException("Senha incorreta ou arquivo adulterado.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encKey);
        }

        using var ms     = new MemoryStream(payload);
        using var zlib   = new ZLibStream(ms, CompressionMode.Decompress);
        using var reader = new StreamReader(zlib, Encoding.UTF8);
        var json = reader.ReadToEnd();
        CryptographicOperations.ZeroMemory(payload);
        return json;
    }
}
