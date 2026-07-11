# 11. Test and acceptance plan

[<- Implementation plan](10-Implementation-Plan.md) | [Home](Home.md) | [Operations and deployment ->](12-Operations-and-Deployment.md)

This plan defines how the .NET 10 console recreation proves source parity and safe production behavior. It is not permission to fill a source gap with a plausible result. Every expected legacy outcome must come from checked-in source, a byte-counted fixture, or a named characterization result under [Documentation conventions](Documentation-Conventions.md#source-precedence). Deliberate corrections run as separate `Safe` tests; observed quirks run only as individually named `StrictLegacy` tests.

The source-facing baselines are the [functional requirements](03-Functional-Requirements.md#requirements-index), [online screen contract](04-Online-Screens-and-Navigation.md#transaction-and-screen-catalog), [batch oracle](05-Batch-Processing.md#deterministic-fixture-oracle), [optional protocols](07-Optional-Modules-and-Integrations.md#integration-data-contracts), and [record layouts](Appendix-File-and-Record-Layouts.md#layout-index).

## Parity gates

The gates are cumulative. A later gate cannot waive an earlier failure.

| Gate | Required evidence | Pass condition |
|---|---|---|
| P0 - evidence freeze | source inventory, fixture manifest and hashes, requirement IDs, compatibility decisions | Every expected value identifies its source or derivation; unresolved behavior is marked as a decision and is not silently asserted. |
| P1 - codec parity | fixed-record, EBCDIC, overpunch, `COMP`, `COMP-3` and report goldens | Every selected layout accepts its valid fixture, rejects wrong lengths, preserves required filler/leading zeroes, and round-trips byte-for-byte where source bytes are complete. |
| P2 - domain/use-case parity | validation order, arithmetic, relationships, mutation order and exit results | Pure and application tests pass for every selected `FR-*` and `DATA-*` requirement. |
| P3 - terminal parity | 24x80 virtual-terminal snapshots, field catalog and logical-key traces | All 17 base screens and each selected optional screen keep every field/cursor inside 24x80 and pass their state/key matrix. |
| P4 - batch parity | immutable supplied fixtures and the exact oracles below | `StrictLegacy` reproduces the published counts, amounts, output record counts and observed defects exactly. |
| P5 - safe behavior | security, authorization, atomicity, concurrency, fault and restart suites | `Safe` is the default, no security weakness is switchable, and every logical mutation survives the specified conflicts and crash points without partial or duplicate effect. |
| P6 - console/deployment | end-to-end process tests and an isolated deployment rehearsal | The one `net10.0` executable honors commands, streams, exit codes, configuration validation, migrations, cancellation, redaction and restart procedures. |
| P7 - release trace | machine-generated requirement/test/source report | Every in-scope requirement and artifact is accounted for, with no unexplained skipped test or unapproved difference. |

### Strict and safe test pairing

| Concern | `StrictLegacy` assertion | `Safe` assertion |
|---|---|---|
| validation and formatting | Preserve observed order, fixed widths, literal messages and a specifically approved quirk. | Preserve the documented business rule while applying the approved correction. |
| posting failure | A compatibility orchestrator may expose category -> account -> transaction partial-write order. | One input produces all accepted effects or one reject; a fault produces neither a partial accepted result nor a duplicate on retry. |
| interest EOF | Skip the final account update while retaining its interest transaction. | Apply interest and reset cycles for the final account in the same account unit of work. |
| report EOF/range | Preserve the duplicated final amount, missing final card total and observed range control flow in a golden formatter. | Process every in-range row once and emit complete page/card/grand totals. |
| statements | Preserve the bounded 51-card x 10-transaction table and fixed 80/100-byte artifacts when explicitly selected. | Remove the bounds defect, escape HTML, produce structurally complete output and surface write failure. |
| credentials/authorization | No strict profile may restore plaintext production passwords, role trust, missing ownership checks, secret logging or non-atomic authorization effects. | Hash credentials, authorize every use case, redact sensitive values and use safe atomic boundaries. |

A known difference needs two tests with the same fixture and distinct expected results, plus its decision/defect ID. There is no global "all bugs" profile. See [compatibility profiles](09-DotNet-Target-Architecture.md#compatibility-profiles).

No performance rate or latency is inferred from the COBOL repository. Measurements may record a baseline, but a release threshold exists only after an approved workload, environment and numeric target are added to the release profile.

## Test layers

| Layer | Scope | Required double/environment | Typical proof |
|---|---|---|---|
| source characterization | COBOL branches, fixture arithmetic and byte layouts | immutable repository snapshot; independent calculator where useful | expected values committed before implementation |
| unit | value objects, validation priority, dates, money/rates, paging and decisions | injected clock/ID; no filesystem/database | boundaries and first-error result |
| codec/property | every selected record/message encoding | spans/streams and deterministic encodings | exact length/offset, round-trip, malformed sign/nibble/character |
| application service | one use case and transaction boundary | in-memory ports plus write-order/fault recorder | result, calls, commit/rollback and audit intent |
| persistence integration | mappings, constraints, migrations, locks and concurrency | isolated SQLite database; selected server adapter when in scope | schema round-trip, relationship enforcement, optimistic conflict |
| virtual terminal | renderer/controller state machine | deterministic 24x80 terminal and scripted logical keys | screen, cursor, attributes, field edits and route |
| protocol contract | optional CSV/fixed messages and queue semantics | local durable queue plus vendor test environment when selected | wire golden, correlation, duplicate delivery and retry |
| process end-to-end | published `carddemo` executable | temporary config/database/files and redirected streams | exit code, stdout/stderr, artifacts and durable state |
| fault/restart | every external write/ack boundary | failpoint-enabled ports and child-process termination | no partial/duplicate effect and resumable ledger |
| security/deployment | trust boundaries and packaged product | isolated non-production deployment | authorization, masking/redaction, config, migration and recovery |

Tests must not depend on workstation culture, local time zone, physical console, global current directory, real user profile, production queue, or execution order. Freeze clock and ID suffixes for goldens, use invariant parsing, and allocate a unique temporary root/database for parallel tests.

## Characterization fixtures

Fixture bytes are immutable inputs. A manifest records repository-relative path, byte length, record count, codec, SHA-256 and provenance. Tests fail before business assertions if the manifest differs. These facts come from [the supplied fixture inventory](Appendix-File-and-Record-Layouts.md#supplied-fixtures); counts are vectors, not production limits.

| Fixture | Records | Length | Required characterization |
|---|---:|---:|---|
| account | 50 | 300 | `ACCDATA.PS` and `ACCTDATA.PS` are byte-identical; signed balances/filler decode |
| card | 50 | 150 | ASCII/EBCDIC semantic equivalence and exact card/account widths |
| cross-reference | 50 | 50 | 36-character ASCII right-pads 14 filler bytes; EBCDIC is full length |
| customer | 50 | 500 | exact offsets, leading zeroes and sensitive-data redaction |
| daily transaction | 300 | 350 | 250 positive and 50 negative overpunch amounts |
| daily initialization | 1 | 350 | EBCDIC-only record uses the same transaction codec |
| disclosure group | 51 | 50 | group/type/category key and rates |
| category balance | 50 | 50 | initial zero balances and composite key |
| transaction category/type | 18 / 7 | 60 / 60 | ordered codes/descriptions |
| user security | 10 | 80 | EBCDIC import; plaintext fixture credential is migrated, never retained |
| branch export | 500 | 500 | 50 customer, 50 account, 50 xref, 300 transaction, 50 card |

Before posting, independently assert: 50 accounts/cards/xrefs/customers/initial category rows; 300 daily rows; 250 `01/0001` and 50 `03/0001`; 250 positive and 50 negative values totaling `104,801.54`; initial account-balance sum `12,269.00`; and resolvable cards/types/categories. See [input integrity](05-Batch-Processing.md#input-integrity) and [`CBTRN02C` validation](../Old_Cobol_Code/app/cbl/CBTRN02C.cbl#L370-L421).

### Batch characterization oracles

| Strict posting measure | Expected |
|---|---:|
| processed / accepted / rejected | 300 / 262 / 38 |
| reject reason | all `0102` |
| accepted amount | 77,954.70 |
| accepted positive / cycle credit | 102,353.99 |
| accepted negative / cycle debit | -24,399.29 |
| rejected amount | 26,846.84 |
| final account-balance sum | 90,223.70 |
| transaction / category-balance rows | 262 / 100 |
| process exit code | 4 |

| Account | Accepted | Rejected | Purchase balance | Credit balance | Posted current balance |
|---|---:|---:|---:|---:|---:|
| `00000000001` | 4 | 2 | 1,164.87 | -70.77 | 1,288.10 |
| `00000000017` | 2 | 4 | 343.77 | -998.33 | -621.56 |
| `00000000030` | 2 | 4 | 29.44 | -930.33 | -898.89 |
| `00000000037` | 1 | 5 | 0.00 | -132.88 | -125.88 |
| `00000000050` | 6 | 0 | 1,501.75 | -47.88 | 1,945.87 |

With cycle ID `2022071800`, strict interest creates 50 IDs `2022071800000001` through `2022071800000050`, including one zero-valued transaction for account 37. Transaction interest totals `1,279.16`. The EOF defect applies only `1,260.39` to accounts, skips account 50's `18.77` update, leaves its cycles intact, resets accounts 1-49, and yields account-balance sum `91,484.09`. Evidence: [`CBACT04C`](../Old_Cobol_Code/app/cbl/CBACT04C.cbl#L393-L515) and [strict interest oracle](05-Batch-Processing.md#strict-interest-result-after-posting).

The paired safe test uses the same 50 transactions and `1,279.16` total, applies all 50 updates, resets all 50 cycle accumulators, and expects balance sum `91,502.86` (`90,223.70 + 1,279.16`). This is the explicit correction in [`FR-BATCH-007`](03-Functional-Requirements.md#batch-and-transfer-requirements), not a COBOL-output claim.

Combining 262 posting rows and 50 interest rows gives 312 master transactions. With malformed statement JCL repaired but program logic unchanged, strict goldens are 50 text statements, 50 concatenated HTML documents, 312 detail rows, 1,262 fixed-80 text records and 6,632 fixed-100 HTML records. Safe tests retain all 50 groups and 312 details but use the approved safe structure; no safe line count is invented.

The repository lacks external `DATEPARM` and its JCL has a separate hard-coded range, so no undocumented report total is an oracle. Report tests use an explicit checked-in range/input fixture and independently calculated included IDs/totals, with strict and safe EOF/range assertions separated ([`CBTRN03C.cbl`](../Old_Cobol_Code/app/cbl/CBTRN03C.cbl#L219-L373)).

## Online acceptance matrix

Every renderer test uses a 24-row x 80-column virtual terminal. The generated [BMS named-field catalog](Appendix-BMS-Field-Catalog.md) is executable test data: across all 21 base/optional BMS files, all 585 named fields must have the documented map, row, column, length, `ATTRB`, color, highlight and initial value. Fail on a write/cursor outside 24x80, input beyond field width, modification of a protected field, echo of a `DARK` field, or a snapshot line other than exactly 80 cells.

The logical-key suite covers Enter, F3, F4, F5, F7, F8 and F12 wherever the [key contract](04-Online-Screens-and-Navigation.md#key-contract) allows them, plus advertised row/confirmation input. PF13-PF24 aliases are asserted only for programs that include [`CSSTRPFY`](../Old_Cobol_Code/app/cpy/CSSTRPFY.cpy#L17-L81). Every state also receives an unsupported key and must show the documented result without mutation.

| Transaction / screen | State and field acceptance | Key/route acceptance | Safe-specific acceptance |
|---|---|---|---|
| `CC00` sign-on | initial, blank user/password, unknown/wrong credential, valid user/admin; 8-cell user and masked password | Enter authenticates; F3 terminates | hash verification, identical outward credential failure, trusted role |
| `CM00` main menu | all 11 entries, blank/nonnumeric/out-of-range | Enter routes; F3 exits | unavailable optional entries do not crash |
| `CA00` admin menu | all 6 entries and invalid-option priority | Enter routes; F3 exits | direct routes re-check admin role |
| `CAVW` account view | 11-cell numeric/nonzero; xref -> account -> customer; every clipped/protected field | Enter fetch; F3 previous/menu | failed lookup stops; no stale buffer; multi-xref choice is not invented |
| `CAUP` account update | fetch, exact validation order, error cursor/attributes, confirm/save/refetch; all literal date/state/ZIP/phone/SSN/FICO boundaries | Enter fetch/validate; F5 save-state only; F12 refetch; F3 return | version conflict; account+customer atomic; correct ZIP/group |
| `CCLI` card list | blank/one/both filters, 0/1/7/8 rows, first/last page; strict uppercase `S/U` | Enter search/action; F7/F8; F3 | next page uses next matching row |
| `CCDL` card view | numeric nonzero account/card and all mapped values | Enter fetch; F3 | card must belong to account |
| `CCUP` card update | fetch, name/status/month/year validation, confirmation; hidden day retained | Enter validate; F5 save; F12 refetch; F3 | full date valid, CVV preserved, ownership/version atomic |
| `CT00` transaction list | blank/exact/lower-bound decision, 0/1/10/11 rows, first `S/s` | Enter search/action; F7/F8; F3 | deterministic paging under concurrent change |
| `CT01` transaction view | valid/missing ID and every mapped field | Enter fetch; F4 clear; F5 list; F3 | read-only path takes no update lock |
| `CT02` transaction add | account precedence/card fallback/both absent; source validation order; yes/no; latest-row copy | Enter validate/confirm/add; F4; F5; F3 | invalid data cannot insert; unique ID/atomic insert |
| `CR00` report request | monthly/yearly/custom priority, dates, confirm/cancel/submitted | Enter state transitions; F3 | exactly one selector, start <= end, durable typed request |
| `CB00` bill payment | numeric account, missing/nonpositive/positive balance, yes/no | Enter fetch/confirm/pay; F4; F3 | atomic payment+balance; no double-pay |
| `CU00` user list | blank/lower-bound, 0/1/10/11 rows, first `U/u/D/d`, Add route | Enter search/action; F7/F8; F3 | admin rechecked; stable paging |
| `CU01` user add | strict first/last/ID/password/type order and duplicate ID | Enter add; F4; F3; strict F12 invalid | usable normalized ID, `A/U`, hash and audit |
| `CU02` user update | fetch, no-change/change, exact order and write errors | Enter fetch; F4; F5 save; F12 return; strict F3 save/return | separate Save/Return; conflict stays; audit |
| `CU03` user delete | fetch, missing user and result | Enter fetch; F4; F5; F3/F12 | confirmation; block self/final-admin delete; audit |

Each row needs initial, success, each validation boundary, first-error priority, not-found/duplicate where applicable, every legal key per state, one illegal key, persistence assertion and return-route assertion. Coordinates/widths come from BMS rather than duplicate hand constants ([`COSGN00.bms`](../Old_Cobol_Code/app/bms/COSGN00.bms#L19-L205) is the sign-on example).

## Batch acceptance matrix

| Workload | `StrictLegacy` acceptance | `Safe` acceptance | Failure/operational acceptance |
|---|---|---|---|
| refresh masters | exact codecs, keys, fixture counts and replacement ordering | validate inputs/relationships before swap | missing/short/duplicate input leaves prior store usable |
| post transactions | exact 300/262/38 `0102` oracle, amounts, account vectors, 262 transactions, 100 categories, exit 4 | same baseline result absent an approved extra rule; per-row all-effects-or-reject | fail around category/account/transaction/reject/ledger; retry by fingerprint |
| calculate interest | 50 transactions, `1,279.16`; skipped `18.77`; applied `1,260.39`; balance `91,484.09` | all accounts; `1,279.16`; balance `91,502.86`; all cycles reset | duplicate cycle, missing rate/xref, every transaction/account/reset failpoint |
| combine/rebuild | 262 + 50 = 312 in required deterministic order/index | idempotent rebuild from committed source | active index is entirely old or entirely new |
| transaction report | explicit range; strict range/EOF/page/card/grand quirks; 133 bytes | every included row once; complete final totals | empty/single/page boundary/card change/missing reference/write failure |
| statements | 50 text, 50 HTML, 312 details, 1,262 x 80 and 6,632 x 100 | 50 groups, 312 details, unbounded rows, escaped complete HTML | 0/1/10/11 transactions/card, >51 cards, relation/disk/restart faults |
| export branch | 500-byte codec and 500-row composition; hard-coded `0001/NORTH` characterization | consistent snapshot; sequence bytes 28-31; approved filter only | concurrent change and temporary-output publication |
| import branch | dispatch `C/A/X/T/D`; 132-byte unknown error and legacy return | validate header/sequence/payload/relationships; explicit outputs; nonzero invalid result | missing card output, unknown/duplicate/truncated row, staging/commit/retry |
| diagnostics | account/card/customer/xref plus `CBACT01C` transformed/array/12/39-byte records | approved diagnostic/codec commands only | read/write/close failure names file, record and field |
| full cycle/pending reports | only approved legacy dependencies; no scheduler inference | durable run graph, lock, restartable steps and atomic claim | cancel/restart after each step without duplication |

Posting tests cover reasons `0100` missing xref, `0101` missing account, `0102` over limit and `0103` expired; expiration overwrites over-limit when both apply ([`CBTRN02C.cbl`](../Old_Cobol_Code/app/cbl/CBTRN02C.cbl#L370-L421)). Strict mode adds no absent validation for status, duplicate ID, merchant data or timestamp syntax.

## Optional-module acceptance matrix

An optional module is either selected and fully gated or disabled. Disabled navigation/commands return the documented unavailable result and do not fail startup. Selected adapters pass local durable-port tests and vendor integration tests in the release environment.

| Module/contract | Characterization acceptance | Safe/integration acceptance | Evidence |
|---|---|---|---|
| CTLI list | seven managed rows; filters/order; `U/D`; F2/F3/F7/F8/F10; dummy BMS row excluded | keyset paging, unbounded page, optimistic conflict | [CTLI](07-Optional-Modules-and-Integrations.md#ctli-list-workflow) |
| CTTU maintenance | lookup/create/update/delete/F4/F5/F12; type 2 and description 50 validation | trimmed value, references, real status, concurrency token | [`COTRTUPC.cbl`](../Old_Cobol_Code/app/app-transaction-type-db2/cbl/COTRTUPC.cbl#L577-L974) |
| type batch/extract | exact 53-byte `A/U/D/*`; seven seed types/eighteen categories; ordered 60-byte output | one validated service, consistent pair and approved refresh graph | [synchronization](07-Optional-Modules-and-Integrations.md#batch-maintenance-and-synchronization) |
| authorization request/reply | 18 CSV fields; six-field trailing-comma response; `00` full or `05` zero; 63 logical bytes and first pointer put of 64 | versioned strict parser; reject malformed tokens/numerics/dates/codes/amount; fresh response | [request](07-Optional-Modules-and-Integrations.md#authorization-mq-request), [response](07-Optional-Modules-and-Integrations.md#authorization-mq-response) |
| authorization decision | lookup/summary precedence; reason priority `3100/4100/4200/4300/5100/5200/9000`; identify unreachable source reasons | approved table explicit; no state leak between messages | [`COPAUA0C.cbl`](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUA0C.cbl#L438-L718) |
| pending state | 100-byte summary, 200-byte detail, reverse date/time, count/total changes | summary+detail+outbox atomic; duplicate has one effect | [segments](07-Optional-Modules-and-Integrations.md#pending-authorization-ims-segments) |
| CPVS/CPVD | five newest rows, `S/s`, F3/F7/F8; detail Enter/F3/F5/F8 and `F/R` | exactly one current selection; no stale flow; unbounded paging; fraud atomic | [CPVS](07-Optional-Modules-and-Integrations.md#cpvs-pending-summary-inquiry), [CPVD](07-Optional-Modules-and-Integrations.md#cpvd-detail-and-fraud-toggle) |
| purge | parse `DD,FFFFF,PPPPP,Y`; characterize date/root/checkpoint defects | reconciled aggregates, approved retention, bounded batches/restart cursor | [`CBPAUP0C.cbl`](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/CBPAUP0C.cbl#L98-L324) |
| IMS load/unload | roots 100; standard details 206 = parent 6 + child 200; GSAM 100/200 and missing-parent defect | invalid read/key terminates; ownership/duplicate policy/restart explicit | [load/unload](07-Optional-Modules-and-Integrations.md#load-and-unload-behavior) |
| account inquiry | `INQA` bytes 1-4, account bytes 5-15, contiguous labeled 1,000-byte response | validate numeric/length; configured reply/correlation; idempotent | [`COACCT01.cbl`](../Old_Cobol_Code/app/app-vsam-mq/cbl/COACCT01.cbl#L390-L499) |
| system date | every request; exact 46 meaningful date/time characters padded to 1,000 | injected clock, configured reply, bounded cancellation/retry | [`CODATE01.cbl`](../Old_Cobol_Code/app/app-vsam-mq/cbl/CODATE01.cbl#L339-L403) |
| MQ behavior | strict auth `NO_SYNCPOINT`/ReplyToQ/CorrelId; strict inquiry fixed queue/padding | inbox/outbox, acknowledgement crashes, correlation/retry/dead-letter | [topology](07-Optional-Modules-and-Integrations.md#triggered-server-topology) |
| deployment | missing MQ/IMS/Db2/CSSL and placeholder config are detected | preflight names missing resources without credentials | [deployment](07-Optional-Modules-and-Integrations.md#authorization-deployment) |

Characterize the authorization worker's observed 501-message off-by-one only in strict tests; production uses an approved bound. Broker tests cover empty wait, quiescing, get/put/ack failure, duplicate MsgId, CorrelId, invalid ReplyToQ and redelivery before/after commit.

## Data-codec tests

| Contract | Exact length | Mandatory focus |
|---|---:|---|
| account | 300 | key 11, signed amounts, dates, ZIP/group, 178 filler |
| card | 150 | card 16, account 11, CVV 3, expiration/status, 59 filler |
| customer | 500 | named offsets, 168 filler, sensitive redaction |
| cross-reference | 50 | keys; 36-character ASCII padding |
| transaction/daily | 350 | overpunch amount, two 26-byte timestamps, 20 filler |
| category balance / disclosure | 50 / 50 | composite keys, signed balance/rate |
| transaction type/category | 60 / 60 | 2-byte and 6-byte keys |
| security user | 80 | ID/password fixture fields, role and filler |
| daily reject | 430 | original 350 + reason 4 + description 76 |
| report / date parameter | 133 / 80 | every line; date bytes 1-10 and 12-21 |
| text / HTML statement | 80 / 100 | strict padding and counts |
| statement work transaction | 350 | card+ID re-key and strict truncation/padding |
| branch export | 500 | header 40 + payload 460 |
| import error | 132 | timestamp/type/seven-digit sequence/message/padding |

Optional lengths are 53-byte type maintenance; 153-byte authorization copybook group versus CSV; 63 logical response plus strict first 64-byte put; 100-byte summary; 200-byte detail; 206-byte standard unload child; 100/200-byte GSAM; 122-byte authorization error; and fixed 1,000-byte inquiry/date buffers. Version the fixed and CSV protocols separately.

For every applicable codec test: exact min/max and leading zeroes; one byte short/long, truncation and trailing bytes; ASCII and selected EBCDIC invalid/unrepresentable bytes; positive/negative/zero overpunch (`{`/`A-I` and `}`/`J-R`); valid/invalid `COMP-3` digit/sign nibbles and overflow; big-endian `COMP` boundaries including `00 00 00 01`; non-invariant culture; filler preservation; redacted field/record provenance; and independent parse -> domain -> format offset/byte assertions.

The branch sequence is one-based bytes 28-31. Strict characterization records the conflicting JCL zero-based offset 28, while production uses the declared sequence field ([export-key discrepancy](Appendix-File-and-Record-Layouts.md#export-key-discrepancy)).

## Security tests

Security weaknesses never receive a compatibility switch.

| Boundary | Required tests |
|---|---|
| credential migration | Import the 80-byte fixture, create a hash, verify login, and prove reusable plaintext is absent from database, audit, log, exception, screen model and exported configuration. |
| authentication | Blank priority, unknown/wrong-password indistinguishability, case normalization, masked input and clean session after sign-off/cancellation. |
| authorization | Invoke every privileged application service directly as unauthenticated, regular and admin actors; navigation/editable session role cannot bypass it. |
| object ownership | Try card view/update with unrelated account and pending/detail with stale IDs; return non-disclosing failure and perform no mutation. |
| user administration | Permit only `A/U`, block self/final-admin delete, separate Save/Return, audit actor/target/outcome and handle concurrent change. |
| injection | Treat report parameters, paths, descriptions and messages as data; no JCL/shell execution. Escape safe HTML and test protocol delimiter/control characters. |
| persistence | Parameterized access, unique/composite/foreign keys, optimistic tokens, migration permissions and no secret in SQL/logs. |
| configuration | JSON/environment/CLI precedence, fail before data access, secrets outside committed files and redacted diagnostics. |
| logging/audit | Scan stdout, stderr, logs and audit for password, CVV, SSN, government ID, full EFT ID, queue credential and raw sensitive records; retain correlation/run IDs. |
| files/queues | Restrict configured roots/destinations, reject path escape, publish files atomically and do not echo raw failed payloads. |

Use unique sentinel values in every sensitive field so absence is proven. Exercise success, validation, repository/queue exception, cancellation and fatal startup. See [`FR-AUTH-003/005`](03-Functional-Requirements.md#authentication-and-session), [`FR-USER-003/007/008`](03-Functional-Requirements.md#security-user-administration) and [`NFR-006/007/009`](03-Functional-Requirements.md#non-functional-requirements).

## Failure concurrency restart tests

Every failpoint test records pre-state, invokes one operation, injects failure, restarts with a new process/service scope, retries by policy, then compares durable state and artifacts. In `Safe` mode the only accepted outcomes are complete pre-state or one complete committed result.

| Workflow | Inject before/after | Invariant |
|---|---|---|
| account/customer update | account write, customer write, commit, audit | stale version loses; both entities/audit agree |
| card update | ownership check, rewrite, commit | relationship rechecked; stale edit cannot overwrite |
| transaction add | ID allocate, insert, commit | concurrent writers get unique 16-character IDs; retry one row |
| bill payment | balance read, ID, transaction, account, commit | same positive balance cannot be paid twice; row/balance agree |
| report request | insert, claim, output publish, complete | request processed once or safely reclaimed |
| posting | category, account, transaction, reject, run ledger | daily identity has one accepted result or reject; batches do not interleave |
| interest | transaction, account, cycle reset, checkpoint | cycle idempotent; final account processed; no duplicate interest |
| import/refresh | validation, stage, relationship check, swap | active store entirely old or new |
| export/report/statement | snapshot, write, flush, rename, completion | no published partial artifact; deterministic restart |
| authorization | inbox, decision, summary, detail, outbox, commit, broker put/ack | duplicate/redelivery has one effect and eventual correlated reply |
| fraud toggle | history, detail, commit | both stores agree after restart |
| purge/load | item, aggregate, checkpoint, commit | no skipped/double row; summary reconciles |
| migration | migration transaction and startup check | failed upgrade recoverable; two starters do not race |

Also run two stale sessions for account/card/user/type; two adds and payments synchronized at read/allocation; two posting/full-cycle processes competing for the lock; paging during insert/delete; duplicate file fingerprint/MsgId/cycle ID; `Ctrl+C` during read/transaction/flush/queue wait; and storage-full, permission, malformed record, lost connection, broker and process-kill faults.

Strict non-atomic order is exposed only through a test compatibility orchestrator. Production never intentionally commits a partial financial mutation. See [batch restart](05-Batch-Processing.md#restart-idempotency-and-recovery) and [safe units of work](09-DotNet-Target-Architecture.md#units-of-work).

## Console and deployment tests

Run against the published artifact, not `dotnet run`:

1. verify `net10.0`, one console entry point and no HTTP listener;
2. invoke every [stable command](09-DotNet-Target-Architecture.md#console-command-surface), `--help`, unknown option and missing/invalid required option;
3. assert exit `0`, `2`, `4`, `8`, `12` and `130` at documented boundaries; diagnostics use stderr and primary output uses the requested file/stdout;
4. run interactive, scripted virtual, redirected input/output and non-ANSI cases; redirected output has no cursor/ANSI sequence;
5. test configuration precedence, validation before data access, configured roots and redacted startup errors;
6. initialize, migrate, verify, seed, back up, restore and reopen an isolated database; repeat verify without mutation;
7. publish/smoke-test every OS/architecture/runtime mode named by the release profile from a clean directory;
8. run codecs under non-default culture/time zone and read-only working directory with configured writable data root;
9. send `Ctrl+C` to interactive, batch and workers; verify restart point, disposal and exit 130;
10. preflight selected optional resources and prove disabled modules need no vendor library;
11. verify structured start/end/count/reject/failure events and correlation/run IDs without sensitive values;
12. rehearse upgrade from the last supported schema/fixture and documented rollback/restore.

Scheduler/service-manager tests use [Operations and deployment](12-Operations-and-Deployment.md#exit-codes-and-retries). They must not classify business exit 4 until that runbook defines the workload policy.

## Requirements-to-tests convention

~~~text
<layer>-<requirement>-<profile>-<scenario>
~~~

Examples:

- `CHR-FR-BATCH-018-STRICT-posting-supplied-fixture`
- `TERM-FR-ACCT-003-SAFE-f12-restores-fetched-values`
- `FAULT-FR-BILL-005-SAFE-crash-after-transaction-insert`
- `CODEC-DATA-001-PARITY-export-sequence-big-endian`
- `SEC-FR-AUTH-005-SAFE-direct-admin-use-case-as-user`

Layer prefixes are `CHR`, `UNIT`, `CODEC`, `APP`, `DB`, `TERM`, `PROTO`, `E2E`, `FAULT`, `SEC` and `DEPLOY`. Profile is `PARITY` when strict/safe do not differ, otherwise `STRICT` or `SAFE`.

| Metadata | Rule |
|---|---|
| requirement | one or more exact `FR-*`, `DATA-*` or `NFR-*` IDs |
| profile | parity, strict or safe; never implicit for a known difference |
| evidence | source path with line anchor, or a documentation anchor that supplies evidence |
| fixture | manifest ID/hash, or `none` for generated boundaries |
| decision | required for deliberate deviation or unresolved runtime choice |
| expected | value, state delta, bytes/snapshot, exit code or event |
| environment | unit, SQLite, vendor adapter or published process |

The generated [traceability report](13-Traceability-and-Coverage.md#requirements-to-tests) lists requirement -> tests -> implementation -> evidence -> decision. "No exception" does not cover a requirement; snapshots do not replace state assertions; a unit test does not replace a required codec/process/database/vendor test. A skip names an approved scope exclusion or blocker and cannot be hidden by retries.

## Release acceptance

A candidate is accepted only when:

- P0-P7 pass for the declared profile and `Safe` is the runtime default.
- Clean pinned .NET 10 restore/build/static checks/full tests pass with warnings as errors.
- Strict batch goldens match every published count, amount, ID, record length and account vector; paired safe corrections pass.
- All 17 base and selected optional screens pass 24x80 snapshots; all 21 BMS maps/585 named fields pass geometry, editing, protection, masking and key-state coverage.
- All 44 COBOL programs, 55 JCL files, 21 BMS maps and all 329 inventoried artifacts are covered, classified as non-runtime evidence, or explicitly excluded with rationale.
- Every in-scope functional/data/non-functional requirement has passing tests and source/implementation/decision evidence.
- Security, authorization, redaction and configuration-secret tests pass; strict mode enables no weakness.
- Atomicity, optimistic concurrency, application-lock, duplicate-delivery, process-kill and restart suites pass at every listed boundary.
- Migration/backup/restore, command/stream/exit, cancellation and clean-host deployment rehearsals pass on every release platform.
- Selected optional adapters pass protocol and release-environment integration; omitted modules pass disabled behavior.
- Reports contain no unexplained skip/flaky retry, broken internal/source link, unresolved placeholder or unapproved critical decision.
- Operations owns the tested runbook, recovery points, configuration schema, secret provisioning, monitoring and rollback.
- Any performance gate cites an approved workload/environment/number; without one, the release makes no unsupported performance claim.

Final approval records source/fixture manifest, product commit, test artifact hashes, schema version, release profile, selected modules, resolved decisions and approvers. A source, fixture, codec, schema, compatibility or protocol change invalidates affected and downstream gates.

---

[<- Implementation plan](10-Implementation-Plan.md) | [Home](Home.md) | [Operations and deployment ->](12-Operations-and-Deployment.md)
