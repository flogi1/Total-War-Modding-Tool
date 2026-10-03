using TotalWarModTool.GameProfiles;
using TotalWarModTool.Localization;
using TotalWarModTool.Settings;

namespace TotalWarModTool.App.Tests;

/// <summary>A synthetic game in a temp folder: install folder with a data folder, program folder with a schema, empty roaming and local roots.</summary>
internal sealed class TestGame : IDisposable
{
    public static readonly GameProfile Profile =
        new("testgame", "Test Game", 42, "Test Game", "packs", "PFH5", "schema_test.ron", ["release"], null);

    /// <summary>One row of the table "x_tables": header byte, row count 1, value 42.</summary>
    public static readonly byte[] TableBytes = PackBuilder.Hex("01 | 01 00 00 00 | 2A 00 00 00");

    private const string SchemaText = """
        (
            version: 5,
            definitions: {
                "x_tables": [
                    (
                        version: 0,
                        fields: [
                            (name: "value", field_type: I32, is_key: false, is_reference: None),
                        ],
                        localised_fields: [],
                    ),
                ],
            },
            patches: {},
        )
        """;

    public TestGame(bool withSchema = true)
    {
        Root = new TempDir();
        InstallPath = Root.File("game");
        DataPath = Path.Combine(InstallPath, Profile.DataFolder);
        Directory.CreateDirectory(DataPath);
        Paths = new AppPaths(Root.File("roaming"), Root.File("local"), Root.File("program"));
        GameSettings = new GameSettings { InstallPath = InstallPath };
        Settings = new GlobalSettings();
        Runner = new FakeOperationRunner();
        Log = new FakeAppLog();
        Texts = new AppTexts(new UiText(Root.File("no-locales")));
        if (withSchema)
        {
            Directory.CreateDirectory(Paths.SchemasDirectory);
            File.WriteAllText(Path.Combine(Paths.SchemasDirectory, Profile.SchemaFile), SchemaText);
        }
    }

    public TempDir Root { get; }

    public string InstallPath { get; }

    public string DataPath { get; }

    public AppPaths Paths { get; }

    public GameSettings GameSettings { get; }

    public GlobalSettings Settings { get; }

    public FakeOperationRunner Runner { get; }

    public FakeAppLog Log { get; }

    public AppTexts Texts { get; }

    /// <summary>Writes a PFH5 mod pack (type 3) into the data folder and returns its path.</summary>
    public string AddPack(string name, params (string Path, byte[] Bytes)[] entries)
    {
        var builder = new PackBuilder();
        foreach (var (path, bytes) in entries)
        {
            builder.AddEntry(path, bytes);
        }

        return builder.WriteTo(Path.Combine(DataPath, name));
    }

    public WorkspaceController NewController(ISteamLocationSource? steam = null, IOperationRunner? runner = null) =>
        new(Profile, GameSettings, Settings, Paths, steam ?? new NoSteam(), runner ?? Runner, Log, Texts);

    public void Dispose() => Root.Dispose();
}

/// <summary>A Steam source that finds no Steam installation.</summary>
internal sealed class NoSteam : ISteamLocationSource
{
    public string? GetSteamPath() => null;
}

/// <summary>A Steam source that returns a fixed folder.</summary>
internal sealed class FixedSteam(string path) : ISteamLocationSource
{
    public string? GetSteamPath() => path;
}
