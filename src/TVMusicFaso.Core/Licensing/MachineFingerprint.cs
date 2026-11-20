using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TVMusicFaso.Core.Licensing;

/// <summary>
/// Empreinte stable d'une machine (16 caractères hexadécimaux, par groupes de 4), dérivée de
/// l'identifiant du système : MachineGuid sous Windows, IOPlatformUUID sous macOS, machine-id sous Linux.
/// Elle change si le système est réinstallé ou si la machine est remplacée : il faut alors réémettre la licence.
/// </summary>
public static class MachineFingerprint
{
    private const int HexLength = 16;

    private static readonly Lazy<string?> CurrentValue = new(Compute, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Empreinte de cette machine, ou null si l'identifiant du système est illisible.</summary>
    public static string? Current => CurrentValue.Value;

    /// <summary>Empreinte dérivée d'un identifiant de système (insensible à la casse et aux espaces autour).</summary>
    public static string FromMachineId(string machineId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(machineId);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("TVMF-licence|" + machineId.Trim().ToLowerInvariant()));
        return Group(Convert.ToHexString(hash)[..HexLength]);
    }

    /// <summary>Forme canonique « XXXX-XXXX-XXXX-XXXX » ; chaîne vide si la valeur n'est pas une empreinte.</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var hex = new StringBuilder(HexLength);
        foreach (var character in value)
        {
            if (Uri.IsHexDigit(character))
            {
                hex.Append(char.ToUpperInvariant(character));
            }
            else if (character != '-' && !char.IsWhiteSpace(character))
            {
                return string.Empty;
            }
        }

        return hex.Length == HexLength ? Group(hex.ToString()) : string.Empty;
    }

    /// <summary>Vrai si la valeur est une empreinte complète (tirets et casse libres).</summary>
    public static bool IsWellFormed(string? value) => Normalize(value).Length > 0;

    /// <summary>Compare deux empreintes sans tenir compte de la casse ni des tirets ; une empreinte invalide ne correspond à rien.</summary>
    public static bool Matches(string? left, string? right)
    {
        var normalized = Normalize(left);
        return normalized.Length > 0 && string.Equals(normalized, Normalize(right), StringComparison.Ordinal);
    }

    private static string Group(string hex) =>
        string.Join('-', Enumerable.Range(0, hex.Length / 4).Select(index => hex.Substring(index * 4, 4)));

    private static string? Compute()
    {
        try
        {
            var machineId = ReadMachineId();
            return string.IsNullOrWhiteSpace(machineId) ? null : FromMachineId(machineId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or System.Security.SecurityException or InvalidOperationException
            or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            return null;
        }
    }

    private static string? ReadMachineId()
    {
        if (OperatingSystem.IsWindows())
        {
            // Vue 64 bits explicite : un processus 32 bits lirait sinon une clé redirigée, sans MachineGuid.
            using var machine = Microsoft.Win32.RegistryKey.OpenBaseKey(
                Microsoft.Win32.RegistryHive.LocalMachine,
                Microsoft.Win32.RegistryView.Registry64);
            using var key = machine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return key?.GetValue("MachineGuid") as string;
        }

        if (OperatingSystem.IsMacOS())
        {
            var output = Run("/usr/sbin/ioreg", "-rd1 -c IOPlatformExpertDevice");
            var match = Regex.Match(output ?? string.Empty, "\"IOPlatformUUID\"\\s*=\\s*\"([^\"]+)\"");
            return match.Success ? match.Groups[1].Value : null;
        }

        foreach (var path in new[] { "/etc/machine-id", "/var/lib/dbus/machine-id" })
        {
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }

        return null;
    }

    private static string? Run(string fileName, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });
        if (process is null)
        {
            return null;
        }

        // La sortie d'erreur est vidée en parallèle : un tampon plein bloquerait le programme lancé.
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        var output = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit(5000))
        {
            try
            {
                process.Kill();
            }
            catch (InvalidOperationException)
            {
            }

            return null;
        }

        return output.GetAwaiter().GetResult();
    }
}
