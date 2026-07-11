using CardDemo.Online;

namespace CardDemo.ConsoleApp.Maps;

/// <summary>
/// Resolves a BMS <c>(map, mapset)</c> name pair to a freshly-built <see cref="BmsMap"/> field model. The
/// console host hands handlers the <see cref="BmsMap"/> as the symbolic-map object on
/// <see cref="IScreenIo.SendMap"/> / <see cref="IScreenIo.ReceiveMap"/>; this catalog is how the host
/// front-end materialises that model when a handler names a map.
/// </summary>
/// <remarks>
/// Each entry is a factory so every SEND starts from a clean field model (a fresh DSECT), matching the
/// CICS pseudo-conversational reset of WORKING-STORAGE per task. More maps are registered here as the
/// remaining online handlers are ported; today only the sign-on map exists.
/// </remarks>
public sealed class BmsMapCatalog
{
    private readonly Dictionary<string, Func<BmsMap>> _byMap =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The default catalog wired with every BMS map the console host currently knows.</summary>
    public static BmsMapCatalog Default { get; } = new BmsMapCatalog()
        .Register(SignonMap.MapName, SignonMap.Build);

    /// <summary>Registers a map factory under its DFHMDI map name.</summary>
    public BmsMapCatalog Register(string mapName, Func<BmsMap> factory)
    {
        _byMap[mapName] = factory;
        return this;
    }

    /// <summary>True when a factory is registered for the given map name.</summary>
    public bool Has(string mapName) => _byMap.ContainsKey(mapName);

    /// <summary>Builds a fresh field model for a map name, or throws when the map is unknown.</summary>
    public BmsMap Build(string mapName) =>
        _byMap.TryGetValue(mapName, out var factory)
            ? factory()
            : throw new KeyNotFoundException($"No BMS map registered for '{mapName}'.");

    /// <summary>Builds a fresh field model, or returns <c>null</c> when the map is unknown.</summary>
    public BmsMap? Find(string mapName) =>
        _byMap.TryGetValue(mapName, out var factory) ? factory() : null;
}
