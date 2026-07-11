# JOB SPEC — WAITSTEP

## Source
`Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/jcl/WAITSTEP.jcl`

## Overall Purpose
A **utility / timing-delay job**. It is not part of the business posting, reporting, or backup chains. Its sole function is to **pause (wait) for a configurable number of centiseconds** by invoking the `COBSWAIT` utility program. The wait duration is supplied at runtime via an in-stream `SYSIN` data card expressed in centiseconds (hundredths of a second).

This kind of step is typically used to:
- Introduce a deliberate delay between dependent steps in a job stream (e.g., let a CICS/IMS region settle, allow a file to be released, or space out polling).
- Throttle a scheduling loop.

For the .NET JobControl step-runner this maps to a single **delay/sleep step** whose duration comes from the step's input parameter.

## JOB Card
| Attribute | Value |
|-----------|-------|
| Job name | `WAITSTEP` |
| Description | `'WAIT STEP'` |
| CLASS | `A` |
| MSGCLASS | `0` |
| NOTIFY | `&SYSUID` (submitting user) |

## Steps

### Step 1 — `WAIT`
| Item | Value |
|------|-------|
| Step name | `WAIT` |
| EXEC | `PGM=COBSWAIT` |
| Program / utility | **`COBSWAIT`** — a (vendor/site) COBOL wait/sleep utility. Not an IBM standard utility (not IDCAMS/SORT/IEFBR14) and not a CardDemo `CB*` business program. It reads a duration value from `SYSIN` and suspends execution for that many centiseconds. |
| PARM | *(none — duration is passed via SYSIN data card, not via PARM=)* |
| COND / RC gating | *(none — no COND parameter; this is the only step)* |
| GDG usage | *(none)* |

#### DD / Dataset usage
| DD name | Disposition / Type | Dataset | Direction | Maps to (table / file) |
|---------|--------------------|---------|-----------|------------------------|
| `STEPLIB` | `DISP=SHR` | `AWS.M2.CARDDEMO.LOADLIB` | Read (load library) | Load module library that contains the `COBSWAIT` executable. Not application data. |
| `SYSOUT` | `SYSOUT=*` | Spool | Write | Program/runtime messages to the job log. |
| `SYSIN`  | Instream (`DD *`) | In-stream control card | Read | Supplies the wait duration. No relational table or sequential dataset — it is a parameter input. |

#### SYSIN control data (exact content)
```
00003600      VALUE IN CENTISECONDS
```
- Value: `00003600` centiseconds = **3600 centiseconds = 36 seconds**.
- Format: an 8-character zero-padded numeric field in columns 1–8; the remaining text (`VALUE IN CENTISECONDS`) is a descriptive comment ignored by the program.
- The JCL header comment documents the convention: `WAIT FOR CENTISECONDS IN THE PARM EG: 00003600 = 36 SECONDS`.

> Note: `COBSWAIT` is a custom program, not IDCAMS or SORT, so there are **no DEFINE/REPRO/DELETE cluster statements and no SORT FIELDS control statements**. The only "control statement" is the single SYSIN duration card shown above.

## Conversion Notes (.NET JobControl)
- Implement as a single step: a delay/sleep of `N` centiseconds where `N` is parsed from the first 8 characters of the step's SYSIN input (here `3600` → `36000` ms).
- No file or database I/O. No dataset mapping required.
- `STEPLIB` maps to nothing in .NET (it is only the load-library resolution for the mainframe executable).
- Exit code should be `0` on successful completion of the wait so downstream steps (in any composed job) proceed.
