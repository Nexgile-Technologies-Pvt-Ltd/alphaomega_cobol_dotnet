using CardDemo.ConsoleApp.Maps;
using CardDemo.Data;
using CardDemo.Online;
using CardDemo.Online.Programs;

namespace CardDemo.ConsoleApp;

/// <summary>
/// The single wiring point that turns the 17 ported online CICS handlers (<c>CardDemo.Online.Programs</c>)
/// into a live <see cref="IProgramRegistry"/> and a matching <see cref="BmsMapCatalog"/> over one shared
/// <see cref="RelationalDb"/>. Each handler is registered under its CICS <c>PROGRAM</c> name (the XCTL/LINK
/// target) and its self-declared <c>TRANSID</c> (per <c>_design/specs/optional/CSD_TRANSACTIONS.md</c>),
/// constructed with the shared DB so every transaction sees the same seeded VSAM-equivalent tables.
/// </summary>
/// <remarks>
/// <para>Every handler in <c>CardDemo.Online.Programs</c> is a near-mechanical port of its COBOL
/// <c>PROCEDURE DIVISION</c>; it builds its own <see cref="BmsMap"/> (from the program's static
/// <c>BuildMap()</c>/<c>BuildBmsMap()</c>) and passes that map object straight to
/// <see cref="IScreenIo.SendMap"/>/<see cref="IScreenIo.ReceiveMap"/>. The <see cref="ConsoleScreenIo"/>
/// renderer therefore renders the handler's own model directly; the <see cref="BmsMapCatalog"/> built here
/// is the by-name fallback the renderer uses when a turn names a map without carrying its model (e.g. a
/// fresh RECEIVE after a SEND TEXT), so it is kept in lock-step with the registry — one map factory per
/// registered program.</para>
/// <para>The registry probes each factory once to learn its <c>ProgramName</c>/<c>TransId</c>; the handlers
/// expose a parameterless ctor for that probe, but the factories below always hand back a DB-backed
/// instance so the resolved handler used to run a turn has its data accessor wired.</para>
/// </remarks>
public static class OnlinePrograms
{
    /// <summary>
    /// Builds the program registry for all 17 base online transactions over the shared
    /// <paramref name="db"/>. Program and transaction routing follow the consolidated CSD dispatcher map.
    /// Delegates to <see cref="OnlineProgramRegistry.Build"/> (the registry lives in
    /// <c>CardDemo.Online</c> so headless tests share it without referencing the console executable).
    /// </summary>
    public static ProgramRegistry BuildRegistry(RelationalDb db) => OnlineProgramRegistry.Build(db);

    /// <summary>
    /// Builds the BMS map catalog covering every map driven by the 17 handlers, keyed by DFHMDI map name.
    /// Each entry is the handler's own static map builder, so a by-name lookup produces the same field
    /// model the handler would build itself.
    /// </summary>
    public static BmsMapCatalog BuildMapCatalog()
    {
        return new BmsMapCatalog()
            .Register("COSGN0A", Cosgn00c.BuildMap)      // sign-on
            .Register(Comen01c.MapName, Comen01c.BuildBmsMap)
            .Register(Coadm01c.MapName, Coadm01c.BuildBmsMap)
            .Register(Coactvwc.MapName, Coactvwc.BuildMap)
            .Register(Coactupc.MapName, Coactupc.BuildBmsMap)
            .Register(Cocrdlic.MapName, Cocrdlic.BuildMap)
            .Register(Cocrdslc.MapName, Cocrdslc.BuildMap)
            .Register(Cocrdupc.MapName, Cocrdupc.BuildMap)
            .Register(Cotrn00c.MapName, Cotrn00c.BuildMap)
            .Register(Cotrn01c.MapName, Cotrn01c.BuildMap)
            .Register(Cotrn02c.MapName, Cotrn02c.BuildMap)
            .Register(Cobil00c.MapName, Cobil00c.BuildMap)
            .Register(Corpt00c.MapName, Corpt00c.BuildMap)
            .Register(Cousr00c.MapName, Cousr00c.BuildMap)
            .Register(Cousr01c.MapName, Cousr01c.BuildMap)
            .Register(Cousr02c.MapName, Cousr02c.BuildMap)
            .Register(Cousr03c.MapName, Cousr03c.BuildMap);
    }
}
