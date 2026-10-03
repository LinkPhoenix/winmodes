using System.Text;

namespace WinModes.Core.Tuning;

/// <summary>Read policy locations only; never execute instructions or retain policy data.</summary>
internal static class RegistryPolicyReader
{
    private const int MaximumBytes = 1024 * 1024;
    public static IReadOnlyList<string> Read(string path, TweakHive hive)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (stream.Length > MaximumBytes) throw new InvalidDataException("Policy file exceeds the reading limit.");
        using var reader = new BinaryReader(stream, Encoding.Unicode);
        if (reader.ReadUInt32() != 0x67655250 || reader.ReadUInt32() != 1) throw new InvalidDataException("Unsupported policy file header.");
        var targets = new List<string>();
        while (stream.Position < stream.Length)
        {
            Expect(reader, '[');
            var key = ReadString(reader);
            Expect(reader, ';');
            var name = ReadString(reader);
            Expect(reader, ';');
            _ = reader.ReadUInt32();
            Expect(reader, ';');
            var size = reader.ReadUInt32();
            Expect(reader, ';');
            if (size > stream.Length - stream.Position) throw new InvalidDataException("Truncated policy data.");
            stream.Position += size;
            Expect(reader, ']');
            // Special deletion/security commands require a dedicated source review, not a guessed reset.
            if (name.StartsWith("**", StringComparison.Ordinal)) throw new InvalidDataException("Policy file contains special commands.");
            targets.Add($"{hive}\\{key.TrimEnd('\\')}\\{name}");
        }
        return targets;
    }
    private static string ReadString(BinaryReader reader)
    {
        var text = new StringBuilder();
        for (var count = 0; count < 32768; count++)
        {
            var character = (char)reader.ReadUInt16();
            if (character == '\0') return text.ToString();
            text.Append(character);
        }
        throw new InvalidDataException("Policy string exceeds the reading limit.");
    }
    private static void Expect(BinaryReader reader, char expected)
    {
        if (reader.ReadUInt16() != expected) throw new InvalidDataException("Invalid policy entry delimiter.");
    }
}
