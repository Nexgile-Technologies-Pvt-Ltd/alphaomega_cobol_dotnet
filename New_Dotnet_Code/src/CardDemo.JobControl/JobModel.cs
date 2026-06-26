using CardDemo.Runtime;

namespace CardDemo.JobControl;

/// <summary>
/// A JCL <c>EXEC</c> step: a named unit of work plus the <c>COND=</c> bypass test that JCL applies before
/// the step runs. A step's <see cref="Action"/> returns the process RETURN-CODE (RC) the way the COBOL
/// program / utility would set it; the <see cref="JobRunner"/> records that RC, gates later steps on it,
/// and surfaces an <see cref="AbendException"/> (CEE3ABD / CICS ABEND) as a hard step failure.
/// </summary>
/// <remarks>
/// <para>The body receives a live <see cref="JobContext"/> so it can resolve GDG generations, look at the
/// running database, and consult prior step results. It returns the step's RC (0 = clean, 4 = warning such
/// as CBTRN02C "posted with rejects", &gt;4 = error). Throwing <see cref="AbendException"/> models an abend:
/// the runner converts it to a failed <see cref="StepResult"/> with the program's abend code.</para>
/// </remarks>
public sealed class JobStep
{
    /// <summary>The JCL step name (e.g. <c>STEP05</c>, <c>STEP10</c>); unique within a job for clarity.</summary>
    public string Name { get; }

    /// <summary>The program / utility this step invokes (e.g. <c>CBTRN02C</c>, <c>IDCAMS</c>, <c>SORT</c>).</summary>
    public string Program { get; }

    /// <summary>The work the step performs, returning the process RETURN-CODE it sets.</summary>
    public Func<JobContext, int> Action { get; }

    /// <summary>
    /// The JCL <c>COND=</c> tests on this step. A step is <b>bypassed</b> when <em>any</em> condition is
    /// true, evaluated against the highest RC of the steps that have already run (z/OS semantics). Empty =
    /// always run. Most CardDemo steps code no <c>COND=</c>; <c>TRANBKP STEP10</c> codes <c>COND=(4,LT)</c>.
    /// </summary>
    public IReadOnlyList<CondCode> Conditions { get; }

    /// <summary>
    /// When true, a non-bypass step that fails (RC above the job's <see cref="Job.AbortThreshold"/> or an
    /// abend) stops the rest of the job, even though z/OS itself only stops on an abend. CardDemo's
    /// data-dependent pipelines (e.g. COMBTRAN, TRANREPT) rely on each step's output, so a failed step
    /// must not let later steps run on missing/stale data. Defaults to true.
    /// </summary>
    public bool StopJobOnFailure { get; }

    public JobStep(
        string name,
        string program,
        Func<JobContext, int> action,
        IReadOnlyList<CondCode>? conditions = null,
        bool stopJobOnFailure = true)
    {
        Name = name;
        Program = program;
        Action = action;
        Conditions = conditions ?? [];
        StopJobOnFailure = stopJobOnFailure;
    }
}

/// <summary>
/// One JCL <c>COND=(code,operator[,stepname])</c> bypass test. The step is bypassed if
/// <c>code &lt;operator&gt; RC</c> is true (e.g. <c>COND=(4,LT)</c> bypasses the step when 4 is LESS THAN a
/// prior RC, i.e. a prior step ended with RC &gt; 4). With no step name the comparison uses the highest RC
/// of all steps that have already executed.
/// </summary>
/// <param name="Code">The comparison constant from the COND parameter.</param>
/// <param name="Operator">The relational operator applied as <c>Code OP ReferenceRc</c>.</param>
/// <param name="StepName">
/// The specific prior step whose RC is compared; <c>null</c> compares against the maximum RC so far.
/// </param>
public sealed record CondCode(int Code, CondOperator Operator, string? StepName = null)
{
    /// <summary>Evaluates <c>Code &lt;operator&gt; referenceRc</c> — true means "bypass the step".</summary>
    public bool IsSatisfied(int referenceRc) => Operator switch
    {
        CondOperator.Gt => Code > referenceRc,
        CondOperator.Ge => Code >= referenceRc,
        CondOperator.Eq => Code == referenceRc,
        CondOperator.Ne => Code != referenceRc,
        CondOperator.Lt => Code < referenceRc,
        CondOperator.Le => Code <= referenceRc,
        _ => false,
    };
}

/// <summary>The JCL COND relational operators (<c>GT/GE/EQ/NE/LT/LE</c>).</summary>
public enum CondOperator
{
    Gt,
    Ge,
    Eq,
    Ne,
    Lt,
    Le,
}

/// <summary>
/// A runnable JCL job: an ordered list of <see cref="JobStep"/>s plus the RC at or below which a step is
/// considered "successful" for the purpose of stopping the job. The orchestrating <see cref="JobRunner"/>
/// executes the steps in order, applying each step's <c>COND=</c> gate against the accumulated highest RC.
/// </summary>
public sealed class Job
{
    /// <summary>The JCL job name (e.g. <c>POSTTRAN</c>, <c>INTCALC</c>, <c>ACCTFILE</c>).</summary>
    public string Name { get; }

    /// <summary>The job description from the JOB card (e.g. <c>'POSTTRAN'</c>).</summary>
    public string Description { get; }

    /// <summary>The steps in execution order.</summary>
    public IReadOnlyList<JobStep> Steps { get; }

    /// <summary>
    /// The RC at or below which a completed step counts as "OK" (a step RC strictly above this aborts the
    /// job when <see cref="JobStep.StopJobOnFailure"/> is set). Defaults to 4 so a CBTRN02C "RC=4, posted
    /// with rejects" warning never stops the cycle, mirroring the z/OS convention.
    /// </summary>
    public int AbortThreshold { get; }

    public Job(string name, string description, IReadOnlyList<JobStep> steps, int abortThreshold = 4)
    {
        Name = name;
        Description = description;
        Steps = steps;
        AbortThreshold = abortThreshold;
    }
}
