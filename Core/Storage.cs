using System.IO;

namespace Arcanum.Core;

public class VaultStorage(string path)
{
    public string Path { get; } = path;

    public bool Exists() => File.Exists(Path);

    public Vault Load(string password)
    {
        byte[] bytes = File.ReadAllBytes(Path);
        string json  = Crypto.DecryptVault(bytes, password);
        return Vault.FromJson(json);
    }

    public void Save(Vault vault, string password)
    {
        string json      = vault.ToJson();
        byte[] encrypted = Crypto.EncryptVault(json, password);
        string tmp       = Path + ".tmp";
        try
        {
            File.WriteAllBytes(tmp, encrypted);
            File.Move(tmp, Path, overwrite: true);
            if (OperatingSystem.IsLinux())
                File.SetUnixFileMode(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
            if (File.Exists(tmp)) File.Delete(tmp);
            throw;
        }
    }

    public Vault CreateNew(string password)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var vault = new Vault();
        Save(vault, password);
        return vault;
    }

    public void ChangePassword(Vault vault, string oldPassword, string newPassword)
    {
        Load(oldPassword); // lança AuthenticationException se incorreta
        Save(vault, newPassword);
    }
}
