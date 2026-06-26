using CardDemo.Runtime;
using CardDemo.Data;

namespace CardDemo.JobControl;

/// <summary>
/// The outcome of one <see cref="JobStep"/>: its name/program, the RETURN-CODE it set (or the abend code),
/// and whether it ran, was bypassed by <c>COND=</c>, completed, or abended. The <see cref="JobRunner"/>
/// records one of these per step (in order) so a caller can assert the per-step RC and gating decisions.
/// </summary>
/// <param name="StepName">The JCL step name.</param>
/// <param name="Program">The program / utility invoked.</param>
/// <param name="Status">How the step ended (run / bypassed / abended).</param>
/// <param name="ReturnCode">The RETURN-CODE the step set (0 for a bypassed step, z/OS convention).</param>
/// <param name="AbendCode">The abend code when <see cref="Status"/> is <see cref="StepStatus.Abended"/>.</param>
/// <param name="Message">A human-readable note (the abend message, or "bypassed by COND=...").</param>
public sealed record StepResult(
    string StepName,
    string Program,
    StepStatus Status,
    int ReturnCode,
    string? AbendCode = null,
    string? Message = null);

/// <summary>How a <see cref="JobStep"/> ended.</summary>
public enum StepStatus
{
    /// <summary>The step ran to completion and set a RETURN-CODE.</summary>
    Executed,

    /// <summary>The step was skipped because a <c>COND=</c> condition was met (z/OS "step bypassed").</summary>
    Bypassed,

    /// <summary>The step abended (CEE3ABD / CICS ABEND); modelled as a hard failure.</summary>
    Abended,
}

/// <summary>The aggregate outcome of a whole <see cref="Job"/> run.</summary>
/// <param name="JobName">The job name.</param>
/// <param name="Steps">The per-step results, in execution order.</param>
/// <param name="MaximumRc">The highest RETURN-CODE across all executed (non-bypassed) steps.</param>
/// <param name="Aborted">True if the job stopped early because a step failed/abended.</param>
public sealed record JobResult(
    string JobName,
    IReadOnlyList<StepResult> Steps,
    int MaximumRc,
    bool Aborted)
{
    /// <summary>The result for the named step, or <c>null</c> if it never ran (or does not exist).</summary>
    public StepResult? Step(string name) =>
        Steps.FirstOrDefault(s => string.Equals(s.StepName, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>True when no step abended and no executed step exceeded the abort threshold.</summary>
    public bool Succeeded => !Aborted && Steps.All(s => s.Status != StepStatus.Abended);
}

/// <summary>
/// Runtime state shared across a job's steps: the relational database every step reads/writes, the GDG
/// generation manager (so a <c>(+1)</c> dataset created by one step resolves to the same generation in a
/// later step), the scratch working directory for flat datasets, the clock, and the results of the steps
/// already executed (for inter-step inspection). One context lives for the duration of a single job run.
/// </summary>
public sealed class JobContext
{
    private readonly List<StepResult> _results = [];

    /// <summary>The relational database the job operates over.</summary>
    public RelationalDb Db { get; }

    /// <summary>The GDG generation manager for this run (resolves <c>(0)</c> / <c>(+1)</c> datasets).</summary>
    public GdgManager Gdg { get; }

    /// <summary>A scratch directory for the job's flat (non-table) datasets (reject files, reports, exports).</summary>
    public string WorkDir { get; }

    /// <summary>The clock used for every <c>FUNCTION CURRENT-DATE</c> / timestamp in the job's programs.</summary>
    public IClock Clock { get; }

    /// <summary>The directory holding the EBCDIC <c>.PS</c> seed datasets (for IDCAMS REPRO from a seed).</summary>
    public string? SeedDataDir { get; }

    /// <summary>The directory holding the <c>.cpy</c> copybooks (for the importer / export serializer).</summary>
    public string? CopybookDir { get; }

    /// <summary>The results of the steps executed so far, in order.</summary>
    public IReadOnlyList<StepResult> StepResults => _results;

    /// <summary>The highest RETURN-CODE across the executed (non-bypassed) steps so far (0 if none yet).</summary>
    public int MaximumRc => _results.Where(r => r.Status != StepStatus.Bypassed)
                                    .Select(r => r.ReturnCode)
                                    .DefaultIfEmpty(0)
                                    .Max();

    public JobContext(
        RelationalDb db,
        string workDir,
        IClock? clock = null,
        string? seedDataDir = null,
        string? copybookDir = null,
        GdgManager? gdg = null)
    {
        Db = db;
        WorkDir = workDir;
        Directory.CreateDirectory(workDir);
        Clock = clock ?? SystemClock.Instance;
        SeedDataDir = seedDataDir;
        CopybookDir = copybookDir;
        Gdg = gdg ?? new GdgManager(System.IO.Path.Combine(workDir, "gdg"));
    }

    /// <summary>Resolves a path under the job's working directory (for a flat dataset by simple name).</summary>
    public string Path(string name) => System.IO.Path.Combine(WorkDir, name);

    /// <summary>The RC of a previously executed step (0 if it was bypassed or never ran).</summary>
    public int RcOf(string stepName) =>
        _results.FirstOrDefault(r => string.Equals(r.StepName, stepName, StringComparison.OrdinalIgnoreCase))
            ?.ReturnCode ?? 0;

    internal void Record(StepResult result) => _results.Add(result);
}

/// <summary>
/// Executes a <see cref="Job"/>'s steps in order, reproducing JCL step sequencing: each step's
/// <c>COND=</c> test is evaluated against the accumulated highest RC (z/OS "any condition true = bypass"),
/// the step's RETURN-CODE is recorded and folded into the running maximum, and an
/// <see cref="AbendException"/> from a step is captured as a hard failure that stops the job. A failed
/// (RC above the job's abort threshold) step that opts into <see cref="JobStep.StopJobOnFailure"/> also
/// stops the job, so a data-dependent pipeline never runs a later step on missing upstream output.
/// </summary>
public sealed class JobRunner
{
    /// <summary>Runs <paramref name="job"/> over <paramref name="context"/>, returning the per-step + aggregate result.</summary>
    public JobResult Run(Job job, JobContext context)
    {
        bool aborted = false;

        foreach (JobStep step in job.Steps)
        {
            // COND= gate: bypass the step if ANY condition is satisfied against the reference RC (z/OS).
            if (ShouldBypass(step, context, out string? bypassNote))
            {
                var bypass = new StepResult(step.Name, step.Program, StepStatus.Bypassed, 0, Message: bypassNote);
                context.Record(bypass);
                continue;
            }

            StepResult result;
            try
            {
                int rc = step.Action(context);
                result = new StepResult(step.Name, step.Program, StepStatus.Executed, rc);
            }
            catch (AbendException abend)
            {
                result = new StepResult(
                    step.Name, step.Program, StepStatus.Abended, 16, abend.AbendCode, abend.Message);
            }

            context.Record(result);

            bool failed = result.Status == StepStatus.Abended || result.ReturnCode > job.AbortThreshold;
            if (failed && step.StopJobOnFailure)
            {
                aborted = true;
                break;
            }
        }

        int maxRc = context.StepResults.Where(r => r.Status != StepStatus.Bypassed)
                                       .Select(r => r.ReturnCode)
                                       .DefaultIfEmpty(0)
                                       .Max();
        return new JobResult(job.Name, context.StepResults.ToList(), maxRc, aborted);
    }

    /// <summary>
    /// True if the step's <c>COND=</c> tells z/OS to bypass it. A condition with a step name compares
    /// against that step's RC; otherwise it compares against the running maximum RC. The step is bypassed
    /// when ANY of its conditions evaluates true.
    /// </summary>
    private static bool ShouldBypass(JobStep step, JobContext context, out string? note)
    {
        foreach (CondCode cond in step.Conditions)
        {
            int referenceRc = cond.StepName is null ? context.MaximumRc : context.RcOf(cond.StepName);
            if (cond.IsSatisfied(referenceRc))
            {
                note = $"bypassed by COND=({cond.Code},{cond.Operator.ToString().ToUpperInvariant()}" +
                       (cond.StepName is null ? "" : $",{cond.StepName}") + $") vs RC={referenceRc}";
                return true;
            }
        }
        note = null;
        return false;
    }
}
