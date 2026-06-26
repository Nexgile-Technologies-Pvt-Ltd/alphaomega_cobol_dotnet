namespace CardDemo.JobControl;

/// <summary>
/// The CardDemo JCL members that are <b>online (CICS) configuration</b>, not runnable batch data jobs, and
/// are therefore intentionally <em>not</em> produced by <see cref="CardDemoJobs"/>. They install or modify
/// CICS resources (CSD groups, programs, transactions, files) or quiesce/resume CICS files via SDSF/CEMT —
/// none of which have a relational/batch effect in the .NET model. They are listed here (with the reason)
/// so the coverage matrix can account for every JCL member without pretending to "run" CICS setup.
/// </summary>
public static class OnlineConfigJobs
{
    /// <summary>
    /// The JCL members classified as online-config / non-runnable-batch, each with the reason it is skipped.
    /// </summary>
    public static IReadOnlyList<OnlineConfigJob> All { get; } =
    [
        new("CBADMCDJ", "CICS CSD DEFINE/INSTALL of the CardDemo admin group (programs/maps/transactions)."),
        new("DUSRSECJ", "CICS CSD install for the USRSEC sign-on resources (online security setup)."),
        new("OPENFIL", "SDSF/CEMT brackets that OPEN (enable) the CICS files — online file-state management."),
        new("CLOSEFIL", "SDSF/CEMT brackets that CLOSE (quiesce) the CICS files — online file-state management."),
        new("WAITSTEP", "A timing/wait step (COBSWAIT MVSWAIT) used to pace online resource installs."),
        new("DEFGDGB", "DEFINE GENERATIONDATAGROUP for the batch GDG bases — modeled by GdgManager.Define, not a data job."),
        new("DEFGDGD", "DEFINE GENERATIONDATAGROUP for the daily GDG bases — modeled by GdgManager.Define, not a data job."),
        new("TRANIDX", "DEFINE ALTERNATEINDEX/PATH + BLDINDEX for the transaction AIX — a secondary query in the relational model."),
        new("ESDSRRDS", "VSAM ESDS/RRDS cluster definitions — no relational store (online/file infrastructure)."),
    ];

    /// <summary>True if <paramref name="jclMember"/> is one of the online-config (skipped) members.</summary>
    public static bool IsOnlineConfig(string jclMember) =>
        All.Any(j => string.Equals(j.Member, jclMember, StringComparison.OrdinalIgnoreCase));
}

/// <summary>A JCL member skipped as online configuration, with the reason it is not a runnable batch job.</summary>
/// <param name="Member">The JCL member name.</param>
/// <param name="Reason">Why it is online-config rather than runnable batch.</param>
public sealed record OnlineConfigJob(string Member, string Reason);
