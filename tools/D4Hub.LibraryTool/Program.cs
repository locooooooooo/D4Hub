using D4Hub.Core;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Usage: D4Hub.LibraryTool <d2core-url> [library-root]");
    return 2;
}

try
{
    var reference = D2CoreBuildUrl.Parse(args[0]);
    var libraryRoot = Path.GetFullPath(args.Length == 2
        ? args[1]
        : Path.Combine(Directory.GetCurrentDirectory(), "library"));
    var cacheRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "D4Hub",
        "library-tool-cache");
    using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
    var client = new D2CoreCloudBuildClient(
        httpClient,
        Path.Combine(cacheRoot, "d2core-affix-72698-zhCN.json"));
    var record = await client.FetchAsync(reference);
    new FileBuildLibraryStore(libraryRoot).Save(record);

    var selected = record.Variants.FirstOrDefault(variant => variant.Index == reference.VariantIndex)
        ?? throw new InvalidDataException($"BD 没有第 {reference.VariantIndex + 1} 个变体。");
    Console.WriteLine($"UPDATED d2core:{record.BuildId}");
    Console.WriteLine($"VARIANTS {record.Variants.Count}");
    Console.WriteLine($"SELECTED {selected.Index} {selected.Name}");
    Console.WriteLine($"EQUIPMENT {selected.Equipment.Count}");
    Console.WriteLine($"AFFIXES {selected.Equipment.Sum(item => item.Affixes.Count)}");
    Console.WriteLine($"HASH {record.ContentHash}");
    return 0;
}
catch (Exception exception) when (exception is FormatException or HttpRequestException or InvalidDataException)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
