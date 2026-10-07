using System.Text.Json;
using System.Text.RegularExpressions;

namespace WinModes.Core.Software;

public sealed record InstalledSoftware(string PackageId, string Source, string Version);

public sealed record SoftwareInventory(bool Available, IReadOnlyList<InstalledSoftware> Packages, DateTimeOffset CheckedUtc, string? Error = null)
{
    public static SoftwareInventory Unavailable(string error) => new(false, [], DateTimeOffset.UtcNow, error);
    public InstalledSoftware? Find(SoftwareEntry entry) => Packages.FirstOrDefault(package =>
        package.PackageId.Equals(entry.WingetId, StringComparison.OrdinalIgnoreCase)
        && package.Source.Equals(entry.Source, StringComparison.OrdinalIgnoreCase));

    public static SoftwareInventory Parse(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
        var packages = new List<InstalledSoftware>();
        foreach (var source in document.RootElement.GetProperty("Sources").EnumerateArray())
        {
            var name = source.GetProperty("SourceDetails").GetProperty("Name").GetString() ?? "";
            foreach (var package in source.GetProperty(nameof(Packages)).EnumerateArray())
            {
                var id = package.GetProperty("PackageIdentifier").GetString();
                var version = package.TryGetProperty("Version", out var value) ? value.GetString() ?? "" : "";
                if (!string.IsNullOrWhiteSpace(id)) packages.Add(new(id, name, version));
            }
        }
        return new(true, packages, DateTimeOffset.UtcNow);
    }

    /// <summary>Read installed versions, not exportable versions. Headers and update columns may be localized.</summary>
    public static SoftwareInventory ParseList(string text)
    {
        var packages = new List<InstalledSoftware>();
        foreach (var line in text.Split('\n'))
        {
            var fields = Regex.Split(line.Trim(), @"\s+", RegexOptions.None, TimeSpan.FromSeconds(1));
            var storeIndex = Array.FindIndex(fields, field => Regex.IsMatch(field,
                @"^MSIX\\OpenAI\.Codex_[0-9.]+_(x64|arm64|x86|neutral)__2p2nqsd0c76g0$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));
            if (storeIndex > 0 && storeIndex + 1 < fields.Length)
            {
                var chat = SoftwareCatalog.Entries.Single(entry => entry.Id == "chatgpt");
                packages.Add(new(chat.WingetId!, chat.Source, fields[storeIndex + 1]));
                continue;
            }
            foreach (var entry in SoftwareCatalog.Entries.Where(entry => entry.WingetId is not null))
            {
                var index = Array.FindIndex(fields, field => field.Equals(entry.WingetId, StringComparison.OrdinalIgnoreCase));
                if (index < 1 || index + 1 >= fields.Length - 1 || !fields[^1].Equals(entry.Source, StringComparison.OrdinalIgnoreCase)) continue;
                packages.Add(new(entry.WingetId!, entry.Source, fields[index + 1]));
                break;
            }
        }
        return new(true, packages, DateTimeOffset.UtcNow);
    }
}
