using System.Text.Json;
using System.Text.Json.Serialization;

namespace D4Hub.Core;

public interface IStateStore
{
    BuildDocument Load();
    void Save(BuildDocument document);
}

public sealed class JsonStateStore : IStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public JsonStateStore(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
    }

    public string Path { get; }

    public BuildDocument Load()
    {
        if (!File.Exists(Path))
        {
            return BuildDocument.CreateStarter();
        }

        try
        {
            var json = File.ReadAllText(Path);
            var document = JsonSerializer.Deserialize<BuildDocument>(json, JsonOptions)
                ?? throw new InvalidDataException("The D4Hub state document is empty.");
            document.EnsureValid();
            return document;
        }
        catch (JsonException)
        {
            return BuildDocument.CreateStarter();
        }
        catch (InvalidDataException)
        {
            return BuildDocument.CreateStarter();
        }
    }

    public BuildDocument LoadStrict()
    {
        if (!File.Exists(Path))
        {
            throw new FileNotFoundException("The D4Hub import file does not exist.", Path);
        }

        var json = File.ReadAllText(Path);
        using var parsed = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });
        ValidateImportShape(parsed.RootElement);

        var document = JsonSerializer.Deserialize<BuildDocument>(json, JsonOptions)
            ?? throw new InvalidDataException("The D4Hub import document is empty.");
        ValidateImportModel(document);
        document.EnsureValid();
        return document;
    }

    public void Save(BuildDocument document)
    {
        document.EnsureValid();
        document.UpdatedAt = DateTimeOffset.UtcNow;
        var directory = System.IO.Path.GetDirectoryName(Path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("The state path has no parent directory.");
        }

        Directory.CreateDirectory(directory);
        var temporaryPath = $"{Path}.{Guid.NewGuid():N}.tmp";
        var json = JsonSerializer.Serialize(document, JsonOptions);
        File.WriteAllText(temporaryPath, json);

        try
        {
            if (File.Exists(Path))
            {
                File.Replace(temporaryPath, Path, null);
            }
            else
            {
                File.Move(temporaryPath, Path);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public void Export(BuildDocument document, string path)
    {
        var exportStore = new JsonStateStore(path);
        exportStore.Save(document);
    }

    private static void ValidateImportShape(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("The D4Hub import document must be a JSON object.");
        }

        var schema = RequireProperty(root, "schemaVersion", JsonValueKind.Number);
        if (!schema.TryGetInt32(out var schemaVersion) || schemaVersion != 1)
        {
            throw new InvalidDataException($"Unsupported D4Hub document schema: {schema.GetRawText()}");
        }

        var build = RequireProperty(root, "build", JsonValueKind.Object);
        var sections = RequireProperty(build, "sections", JsonValueKind.Array);
        if (sections.GetArrayLength() == 0)
        {
            throw new InvalidDataException("The D4Hub import document has no build sections.");
        }

        foreach (var section in sections.EnumerateArray())
        {
            if (section.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("The D4Hub import document contains an invalid build section.");
            }

            RequireProperty(section, "items", JsonValueKind.Array);
        }

        RequireProperty(root, "overlay", JsonValueKind.Object);
        var profiles = RequireProperty(root, "profiles", JsonValueKind.Array);
        if (profiles.GetArrayLength() == 0)
        {
            throw new InvalidDataException("The D4Hub import document has no HUD profiles.");
        }

        foreach (var profile in profiles.EnumerateArray())
        {
            if (profile.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("The D4Hub import document contains an invalid HUD profile.");
            }

            var id = RequireProperty(profile, "id", JsonValueKind.String).GetString();
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidDataException("The D4Hub import document contains a HUD profile without an id.");
            }
        }

        var selectedProfileId = RequireProperty(root, "selectedProfileId", JsonValueKind.String).GetString();
        if (string.IsNullOrWhiteSpace(selectedProfileId))
        {
            throw new InvalidDataException("The D4Hub import document has no selected HUD profile.");
        }

        RequireProperty(root, "updatedAt", JsonValueKind.String);
    }

    private static void ValidateImportModel(BuildDocument document)
    {
        if (document.Build is null
            || document.Overlay is null
            || document.Profiles is null
            || document.Profiles.Count == 0
            || document.Build.Sections is null
            || document.Build.Sections.Count == 0)
        {
            throw new InvalidDataException("The D4Hub import document is incomplete.");
        }

        if (document.Profiles.Any(profile => profile is null || string.IsNullOrWhiteSpace(profile.Id)))
        {
            throw new InvalidDataException("The D4Hub import document contains an invalid HUD profile.");
        }

        var profileIds = document.Profiles.Select(profile => profile.Id).ToArray();
        if (profileIds.Distinct(StringComparer.Ordinal).Count() != profileIds.Length)
        {
            throw new InvalidDataException("The D4Hub import document contains duplicate HUD profile ids.");
        }

        if (!profileIds.Contains(document.SelectedProfileId, StringComparer.Ordinal))
        {
            throw new InvalidDataException("The selected HUD profile does not exist in the D4Hub import document.");
        }
    }

    private static JsonElement RequireProperty(JsonElement parent, string name, JsonValueKind kind)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != kind)
        {
            throw new InvalidDataException($"The D4Hub import document requires {name} as {kind}.");
        }

        return value;
    }
}
