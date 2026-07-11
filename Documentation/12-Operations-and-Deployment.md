# 12. Operations and deployment

[<- Test plan](11-Test-and-Acceptance-Plan.md) | [Home](Home.md) | [Traceability ->](13-Traceability-and-Coverage.md)

## Operational workloads

The planned .NET replacement is a single `net10.0` console product. This page defines its target operating model; it does not claim the runtime has been implemented. The self-contained default shall not require CICS, JES, VSAM, IMS or Db2. Interactive sessions, one-shot batch commands and optional long-running workers are modes of the same executable. The target command list is normative in [.NET Target Architecture](09-DotNet-Target-Architecture.md#console-command-surface).

| Workload | Invocation pattern | Lifetime | Exclusive resource |
|---|---|---|---|
| terminal servicing | `carddemo interactive` | one user session | terminal only; database uses ordinary transactions |
| database setup | `database initialize/migrate/verify` | one shot | schema/migration lock |
| master refresh | `batch refresh-masters` | one shot | batch application lock + target data sets/tables |
| posting | `batch post-transactions` | one shot, record checkpoints | posting lock + input fingerprint |
| interest | `batch calculate-interest` | one shot, account checkpoints | interest/cycle lock |
| transaction maintenance | `batch combine-transactions`, `rebuild-transaction-index` | one shot | transaction-maintenance lock |
| reporting | `generate-report`, `generate-statements`, `run-pending-reports` | one shot/request loop | output claim/path; report request lease |
| full cycle | `batch full-cycle --profile <name>` | orchestrates named subcommands | umbrella batch lock |
| branch transfer | `transfer export-branch` / `import-branch` | one shot | snapshot or import lock |
| authorization/inquiry workers | `worker authorization/account-inquiry/system-date` | long running | durable queue consumer lease |
| authorization purge | `authorization purge-expired` | one shot/restartable | purge scope lock/checkpoint |

This mapping replaces the 38 core application JCL jobs and 17 optional/sample JCL files without implying that every platform demonstration becomes a production command. The disposition of each job is in [Batch Workload Catalog](05-Batch-Processing.md#batch-workload-catalog), [Optional Modules](07-Optional-Modules-and-Integrations.md#extension-artifact-coverage), and [Program Catalog](Appendix-Program-Catalog.md#job-and-procedure-catalog).

## Deployment model

### Supported shape

- Publish one console entry point for the chosen OS/architecture, either framework-dependent or self-contained after platform qualification.
- Store the database and mutable work/output files outside the application installation directory.
- Run interactive mode in a real TTY. Run batch/workers under an OS service manager or scheduler with redirected output and a noninteractive identity.
- Permit only one writer-heavy batch/full-cycle process for the SQLite default. Interactive reads/writes remain subject to database locking and optimistic concurrency.
- Use a server relational provider instead of SQLite if approved deployment needs multiple machines or sustained concurrent writers; do not place a SQLite file on an unqualified shared network filesystem.
- Optional IBM MQ and external database adapters are deployed/configured only if the corresponding profile is approved.

No web server, inbound HTTP port or browser tier belongs to this product.

### Directory contract

The exact paths are configuration, not hard-coded legacy data-set names. A recommended layout is:

```text
<install>/                   immutable executable and runtime files
<config>/                    non-secret appsettings files
<data>/carddemo.db           default database
<data>/inbox/                operator-staged immutable inputs
<data>/work/<run-id>/        command-private intermediate data
<data>/out/reports/          committed reports/statements
<data>/out/rejects/          posting/import reject outputs
<data>/archive/<run-id>/     immutable run inputs/outputs/manifests
<data>/backup/               protected database backups
<logs>/                      structured operational logs, if file sink approved
```

Before writing, the application resolves a canonical absolute path and verifies it lies under its configured root unless the operator explicitly permits an external path. Output is written to a run-private temporary name, flushed, then atomically renamed when the command commits. Failed temporary output is retained or deleted according to a configured diagnostic policy that must still protect sensitive data.

## Configuration and secrets

Configuration providers and sections are defined in [Configuration Contract](09-DotNet-Target-Architecture.md#configuration-contract). Operational precedence is command line over `CARDDEMO_` environment variables over environment-specific JSON over base JSON. On startup, each command validates only the sections it needs plus global data/logging options.

Secrets include database/MQ credentials, TLS keys, credential-migration material and any transfer credentials. Supply them through an OS secret store, protected environment injection or approved external provider; do not commit them or echo their values. The checked-in `FTPJCL.JCL` contains inline host, user-ID and password values in its FTP input stream ([`FTPJCL.JCL` lines 33-35](../Old_Cobol_Code/app/jcl/FTPJCL.JCL#L33-L35)). The repository does not prove whether these demo-like values were ever live; the inline-secret pattern is evidence of a weakness, not data to migrate.

Minimum environment separation:

| Environment | Data | External integration | Logging |
|---|---|---|---|
| development | disposable local DB/fixtures | local durable queues/fakes | verbose but redacted |
| automated test | isolated per test/run | deterministic fakes/test containers when approved | captured, asserted redaction |
| acceptance | restored production-shaped anonymized/authorized extract | nonproduction endpoints | production format + test correlation |
| production | protected persistent DB/files | approved endpoints/TLS/secrets | controlled retention/access |

The source does not define log/audit retention, backup RPO/RTO or regulatory classification. Operations must obtain those values as decisions rather than deriving them from VSAM/JCL retention values.

## Data ingestion and provenance

Every batch input gets a manifest before processing:

| Manifest field | Purpose |
|---|---|
| run ID / correlation ID | join logs, audit, checkpoints and outputs |
| command/profile/version | prove which behavior executed |
| canonical path and logical role | distinguish daily, master, disclosure, transfer, etc. |
| byte length, record count, SHA-256 | detect substitution/truncation and identify retries |
| codec/encoding/record length | prevent implicit locale/encoding parsing |
| created/received time and actor | provenance |
| strict/safe compatibility policy names | reproduce result |
| database schema/application version | restore/replay compatibility |

Inputs are staged read-only. Validation runs before mutation where the workflow permits. A successful run archives the manifest, immutable input identity, output hashes/counts and database/run-ledger commit. A retry with the same fingerprint returns the prior outcome or resumes its documented checkpoint; it must not silently process the same daily file twice.

The source inventory ledger itself is reproducible via [`New-SourceInventory.ps1`](tools/New-SourceInventory.ps1), but it inventories the legacy snapshot and is not used as a runtime manifest.

## Core-cycle runbook

The checked-in shell streams, Control-M and CA 7 definitions disagree and omit some jobs; they are cataloged in [Operational Streams and Scheduling](05-Batch-Processing.md#operational-streams-and-scheduling). The following is a **target recommended dependency flow**, not a claim that one legacy schedule implements it exactly.

### Initial load / controlled refresh

1. Back up database and verify restore point.
2. Stage and fingerprint all required fixture/master/reference inputs.
3. Run `database migrate`, then `database verify`.
4. Run `batch refresh-masters --fixture-root <staged-root>` using the selected replace/upsert policy.
5. Run referential, count, record-codec and financial-total reconciliation.
6. Enable interactive traffic only after verification succeeds.

The safe refresh uses one atomic staging/swap or transaction. The legacy close/delete/define/load/open sequence can leave files unavailable after failure and is not the target transaction model.

### Daily posting

1. Stage/fingerprint the daily transaction file and rejects destination.
2. Confirm no completed run exists for the same input/policy.
3. Run `batch post-transactions --input ... --rejects ...`.
4. Treat exit 0 as all accepted, exit 4 as completed with business rejects, and fatal codes as an incomplete run requiring investigation/retry rules.
5. Reconcile input = accepted + rejected; verify accepted financial totals and output hashes.
6. Archive input/reject/manifest/run summary before downstream work.

Do not rerun a fatal posting by copying only its output files. Resume/retry through the run ledger so atomic record commits and prior results are recognized.

### Period/cycle close

1. Confirm all required daily posting runs are complete and reconciled.
2. Record a consistent pre-close transaction/account/category checkpoint.
3. Run `batch calculate-interest --cycle-id <exact-10-char-id>` once; verify account and generated-interest totals.
4. Run combine/rebuild operations if the selected storage/profile needs them, including the generated interest transactions in the rebuilt master.
5. Generate statements and required dated reports from the resulting approved post-interest cycle state.
6. Archive outputs/manifests, then verify the database.

The cycle ID must be unique for the period under the selected ID policy. The legacy `INTCALC.jcl` hard-codes `2022071800`; production shall not copy that value.

### Pending report requests

1. A confirmed interactive request creates a durable structured row.
2. `batch run-pending-reports` leases the oldest ready request.
3. Renderer writes a run-private output and records hash/count.
4. Commit output rename and request completion atomically as far as filesystem/database coordination permits; otherwise use a recoverable outbox state.
5. A transient failure releases/schedules retry; a permanent validation failure moves to failed status with a sanitized message.

Raw embedded JCL is never submitted in the default .NET product.

## Import/export runbook

### Export

- Run against a consistent database snapshot.
- Record selection scope, branch/region values, sequence range, record counts by type and SHA-256.
- Write exactly 500-byte records through the named transfer codec.
- Validate the completed stream by re-reading it before atomic publication.
- Do not claim branch filtering unless the approved selection rule is configured; source export does not filter despite its name.

### Import

- Verify exact length, header/type/sequence, payload, relationship and duplicate rules before applying.
- Stage decoded entities and a reject/error list; unknown type is non-success.
- Apply the accepted import atomically or through an approved restartable staged merge.
- Reconcile counts by `C/A/X/T/D`, relationships, balance totals and output error hash.
- Preserve raw failing record securely for diagnosis without emitting sensitive content to normal logs.

## Optional-worker runbook

Workers validate queue/database configuration before receiving. Each message is handled through durable inbox/outbox/idempotency state. Operational counters include received, duplicate, approved/declined/not-found, retried, dead-lettered and reply-completed, without logging raw request/customer/card data.

Startup/shutdown:

1. acquire consumer identity/lease and check schema/connectivity;
2. poll with bounded wait and honor cancellation;
3. claim/deduplicate request, execute one clean per-message scope, persist decision/outbox;
4. send/confirm reply, then mark outbox/request complete;
5. on shutdown stop receiving, finish or release the current lease, flush logs and exit deterministically.

MQ completion/reason codes, configured queue identity and correlation identifiers belong in sanitized structured fields. Queue credentials and full message bodies do not.

## Exit codes and retries

| Exit | Operational interpretation | Retry policy |
|---:|---|---|
| 0 | completed and committed | do not repeat unless command explicitly supports a new run |
| 2 | CLI/configuration invalid; no business mutation | correct invocation/configuration, then rerun |
| 4 | completed with business rejects/findings | archive/reconcile; retry only rejected records through an approved new input |
| 8 | requested resource/state unavailable | investigate; retry if state becomes available and run ledger shows incomplete |
| 12 | fatal I/O/data/integration failure | investigate logs/checkpoint; restore/resume/retry through command, never ad hoc |
| 130 | cancelled | inspect run status; resume/retry according to checkpoint, not assumed rollback |

Schedulers must treat 0 and, only for commands documented to use it, 4 as successful completion. They must not blanket-accept every code below a site threshold. Retries use exponential/bounded scheduler or worker policy without holding database transactions during delay. No unbounded loop is allowed.

## Observability and operator evidence

Every command emits one start and one terminal structured event with application/schema version, command, profile, run/correlation ID, actor/service identity, elapsed time, sanitized outcome, counts and output identities. Financial totals are allowed in batch summaries; customer/card/account identifiers are redacted or tokenized per security policy.

Health is command-based rather than HTTP:

```text
carddemo database verify
carddemo database verify --check schema
carddemo database verify --check relationships
carddemo database verify --check queues
```

Exact optional flags are finalized during implementation and included in `--help`; this page does not assert that code already exists.

Alerts should be based on failed/fatal runs, stale claimed work, repeated transient integration failures, dead letters, reconciliation mismatch, storage exhaustion and backup/restore failure. Numeric alert thresholds require operational baselines and are intentionally not invented from the demo source.

## Backup, restore and disaster recovery

Before migration, master refresh, import, period close or application/schema upgrade:

1. quiesce writer commands or obtain the application lock;
2. create a provider-consistent database backup plus configuration/version manifest;
3. copy required output/run manifests and queue/outbox state consistently;
4. verify backup integrity and periodically perform a restore rehearsal;
5. record restore point and retention disposition in audit evidence.

Restore never mixes an old database with newer unacknowledged queue replies. After restore, reconcile inbox/outbox/request identifiers before starting workers. Required backup frequency/retention and recovery objectives remain organization decisions.

## Upgrade and rollback

1. Stop new interactive sessions/workers and drain or release message leases.
2. Finish/cancel batch commands and confirm no active run lock.
3. Back up and record current executable/schema/config hashes.
4. Deploy immutable new files alongside the current version.
5. Run explicit `database migrate`, then verification/smoke commands.
6. Start worker then interactive access in controlled order and monitor.
7. Roll back executable only when schema is backward compatible; otherwise restore the paired pre-upgrade database/queue state under the approved migration rollback procedure.

## Operational acceptance criteria

- A clean machine can run the packaged console using only documented prerequisites/configuration.
- Interactive mode fails clearly without a TTY; batch modes work with redirected streams and emit no ANSI control characters.
- Full fixture load/post/interest/report/statement cycle can be operated from the runbook and reconciles to the golden oracle.
- Kill/restart tests prove each mutating command’s documented recovery/idempotency result.
- Concurrent conflicting batch commands are rejected or serialized; no silent double processing occurs.
- Backup is restore-tested, including queue/outbox consistency for selected optional modules.
- Logs/audit/run manifests contain required evidence and no prohibited sensitive values.
- All optional-disabled commands/navigation fail predictably without affecting core.
- Every site-specific external dependency has an approved configuration, credential, retry and support owner or is explicitly excluded.

---

[<- Test plan](11-Test-and-Acceptance-Plan.md) | [Home](Home.md) | [Traceability ->](13-Traceability-and-Coverage.md)
