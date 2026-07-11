# 13. Traceability and coverage

[<- Operations](12-Operations-and-Deployment.md) | [Home](Home.md) | [Known defects ->](14-Known-Defects-and-Open-Decisions.md)

## Coverage summary

This wiki documents the supplied snapshot; it does not claim that the .NET product has already been implemented. Coverage is measured at three levels:

1. **Inventory coverage:** every source artifact is in the generated hash ledger.
2. **Behavioral coverage:** executable/declarative artifacts are mapped to a capability, contract, support role, contradiction or explicit non-product disposition.
3. **Delivery coverage:** each requirement will link to .NET implementation and automated tests when those exist.

| Measure | Supplied | Documentation evidence | Status |
|---|---:|---|---|
| all artifacts | 329 | [`source-inventory.csv`](appendices/source-inventory.csv), [inventory page](Appendix-Source-Inventory.md#inventory-snapshot) | inventoried |
| COBOL programs | 44 | [Program Catalog](Appendix-Program-Catalog.md#cobol-program-catalog) | behavior/disposition mapped |
| JCL files | 55 | [Job and Procedure Catalog](Appendix-Program-Catalog.md#job-and-procedure-catalog) | behavior/disposition mapped |
| BMS sources | 21 | [BMS Field Catalog](Appendix-BMS-Field-Catalog.md#catalog) | 585 named fields mapped |
| COBOL/generated BMS copybooks | 62 | [layouts](Appendix-File-and-Record-Layouts.md#layout-index), [screens](04-Online-Screens-and-Navigation.md#transaction-and-screen-catalog), [optional contracts](07-Optional-Modules-and-Integrations.md#integration-data-contracts), inventory | contract/support mapped |
| CICS resource-definition files | 4 | [architecture](02-System-Context-and-Architecture.md#legacy-runtime-context), [security](08-Security-and-Controls.md#legacy-security-model), [optional deployment](07-Optional-Modules-and-Integrations.md#module-boundaries) | resources/gaps mapped |
| Db2 DDL/DCL | 9 | [transaction reference entities](07-Optional-Modules-and-Integrations.md#transaction-reference-entities), [fraud row](07-Optional-Modules-and-Integrations.md#fraud-db2-row) | schema/contract mapped |
| IMS DBD/PSB | 8 | [pending IMS segments](07-Optional-Modules-and-Integrations.md#pending-authorization-ims-segments) | hierarchy/access mapped |
| assembler/macros | 4 | [support-program catalog](Appendix-Program-Catalog.md#assembler-and-native-helpers) | caller/disposition mapped |
| procedures | 6 | [job/procedure catalog](Appendix-Program-Catalog.md#job-and-procedure-catalog) | orchestration/build role mapped |
| scheduler definitions | 2 | [scheduling](05-Batch-Processing.md#operational-streams-and-scheduling) | contradictions mapped |
| scripts | 9 shell + 1 AWK | [scheduling/automation](05-Batch-Processing.md#operational-streams-and-scheduling), inventory | assumptions/gaps mapped |
| utility control statements | 8 | [batch/optional workload pages](05-Batch-Processing.md#batch-workload-catalog), [optional modules](07-Optional-Modules-and-Integrations.md#extension-artifact-coverage) | utility role mapped |
| fixture/captured data | 24 binary/EBCDIC/text | [layouts](Appendix-File-and-Record-Layouts.md#layout-index), [fixture oracle](05-Batch-Processing.md#deterministic-fixture-oracle) | parsed/characterized |
| diagrams/images | 12 | [source precedence](Documentation-Conventions.md#source-precedence), inventory | secondary evidence only |
| markers/placeholders | 48 | inventory and [automation discussion](05-Batch-Processing.md#operational-streams-and-scheduling) | non-behavioral/unknown build role |
| repository/build metadata | remainder | [Source Inventory](Appendix-Source-Inventory.md#inventory-snapshot) | provenance/support mapped |

Counts are generated from the snapshot, not typed from memory. `New-SourceInventory.ps1` recomputes path, area, kind, size, line count and SHA-256. `New-BmsFieldCatalog.ps1` recomputes the 21-map/585-field appendix.

## Requirements-to-source

| Requirement range | Primary legacy evidence | Detailed interpretation | Target architecture/use case |
|---|---|---|---|
| `FR-AUTH-001`-`006` | `COSGN00C`, sign-on BMS, `CSUSR01Y`, `COCOM01Y`, base CSD | [Sign-on](04-Online-Screens-and-Navigation.md#sign-on), [security](08-Security-and-Controls.md#legacy-security-model) | interactive authentication/session |
| `FR-ACCT-001`-`010` | `COACTVWC`, `COACTUPC`, account/customer/xref copybooks/maps | [Account workflows](04-Online-Screens-and-Navigation.md#account-view) | account query/update use cases |
| `FR-CARD-001`-`008` | `COCRDLIC`, `COCRDSLC`, `COCRDUPC`, card maps/layouts | [Card workflows](04-Online-Screens-and-Navigation.md#card-list) | card query/update use cases |
| `FR-TRAN-001`-`011` | `COTRN00C`-`02C`, maps, transaction/xref layouts | [Transaction workflows](04-Online-Screens-and-Navigation.md#transaction-list) | transaction query/add/ID allocation |
| `FR-RPT-001`-`003` | `CORPT00C`, BMS, `JOBS` CSD definition | [Report request](04-Online-Screens-and-Navigation.md#report-request) | durable report-request use case |
| `FR-RPT-004`-`006` | `CBTRN03C`, `CBSTM03A/B`, `TRANREPT`, `CREASTMT` | [Reports/statements](05-Batch-Processing.md#transaction-report-cbtrn03c) | report/statement commands |
| `FR-BILL-001`-`005` | `COBIL00C`, BMS, transaction/account layouts | [Bill payment](04-Online-Screens-and-Navigation.md#bill-payment) | atomic full-balance payment |
| `FR-USER-001`-`008` | `COUSR00C`-`03C`, maps, user copybook | [User administration](04-Online-Screens-and-Navigation.md#user-add-update-and-delete) | privileged user CRUD/audit |
| `FR-BATCH-001`-`018` | core batch COBOL, 38 `app/jcl` jobs, procedures/schedulers/scripts/fixtures | [Batch specification](05-Batch-Processing.md#batch-workload-catalog) | batch/transfer commands and run ledger |
| `FR-OPT-001`-`004` | 27 transaction-type module artifacts | [Db2 type module](07-Optional-Modules-and-Integrations.md#transaction-type-db2-module) | optional type maintenance/import/export |
| `FR-OPT-005`-`013` | 40 authorization module artifacts | [Authorization module](07-Optional-Modules-and-Integrations.md#authorization-imsdb2mq-module) | optional auth worker/screens/purge/codecs |
| `FR-OPT-014`-`017` | four VSAM/MQ module artifacts | [VSAM/MQ servers](07-Optional-Modules-and-Integrations.md#vsammq-account-and-date-servers) | optional inquiry/date workers |
| `DATA-001`-`009` | copybooks, DDL/DCL/DBD/PSB, data/JCL keys | [Layouts](Appendix-File-and-Record-Layouts.md#layout-index), [data model](06-Domain-Data-Model.md#logical-relationship-model) | persistence schema and codecs |
| `NFR-001`-`011` | derived from all runtime/data contracts plus user’s .NET 10 console constraint | [Target architecture](09-DotNet-Target-Architecture.md#target-constraints) | solution-wide acceptance |

The consolidated wording and track are in [Functional Requirements](03-Functional-Requirements.md#requirements-index). When a detailed page and this matrix differ, the detailed source-linked rule and the decision register take precedence; the inconsistency must then be corrected here.

## Artifact coverage

### Core online

| Artifact family | Coverage rule |
|---|---|
| 17 online COBOL controllers | one row each in [online transaction catalog](04-Online-Screens-and-Navigation.md#transaction-and-screen-catalog) and [program catalog](Appendix-Program-Catalog.md#core-online-programs) |
| 17 online BMS maps + generated symbolic copybooks | exact fields/keys/workflow in [Online Screens](04-Online-Screens-and-Navigation.md#field-and-workflow-specifications); every named BMS field in [BMS Catalog](Appendix-BMS-Field-Catalog.md#catalog) |
| shared/domain copybooks | entity/relationship/layout or validation-table evidence in [Data Model](06-Domain-Data-Model.md), [Layouts](Appendix-File-and-Record-Layouts.md), and screen validation sections |
| base CSD | files/programs/transactions/TDQ plus orphan/security/recovery gaps in [Architecture](02-System-Context-and-Architecture.md#base-online-topology) and [Security](08-Security-and-Controls.md) |

### Core batch

All 14 core batch/utility COBOL sources are individually classified in [Program Catalog](Appendix-Program-Catalog.md#batch-and-utility-programs). All 38 `app/jcl` files have an observed purpose and target disposition in [Batch Workload Catalog](05-Batch-Processing.md#batch-workload-catalog), including stale, malformed, support/demo and external-only jobs. Procedures, assembler helpers, control cards, schedules and scripts are not promoted to financial features; their operational effect/gap is documented.

### Optional packages

[Optional Modules - Extension Artifact Coverage](07-Optional-Modules-and-Integrations.md#extension-artifact-coverage) enumerates all 71 optional artifacts and reconciles them to the filesystem: 27 transaction-type/Db2, 40 authorization/IMS/Db2/MQ and 4 VSAM/MQ. The page distinguishes executable contracts, deployment assets, data, missing dependencies and prose contradictions.

### Samples, diagrams and automation

Sample compile JCL/procedures and platform examples establish build assumptions, not product behavior. Diagram source/images and READMEs are secondary evidence. Marker files are zero-content or name-only automation signals; their exact build semantics are not inferable without the missing external build environment. Every one remains hash-inventoried so it cannot be silently overlooked.

## Requirements-to-tests

No .NET tests exist in this documentation-only task. The following naming and evidence convention is the handoff contract:

```text
<RequirementId>_<Scenario>_<ExpectedResult>

Examples:
FR_AUTH_004_AdminRole_RoutesToAdminMenu
FR_ACCT_008_CustomerWriteFails_RollsBackAccount
FR_BATCH_003_AnyBusinessReject_ReturnsExit4
FR_OPT_007_CrashAfterDecisionBeforeSend_ReplyIsRecoveredOnce
DATA_005_TransferPackedDecimal_RoundTripsGoldenBytes
```

Every automated test case recorded in the future traceability manifest shall contain:

| Field | Required content |
|---|---|
| requirement IDs | one or more stable IDs from `03` |
| profile | `Safe`, `StrictLegacy`, or target-only operational/security |
| source evidence | deep link(s) or fixture manifest/oracle row |
| preconditions/data | fixture/version, clock, culture, schema and compatibility switches |
| action | UI keys/fields, command/options or worker message |
| expected | view/output bytes, state mutations, audit/log/exit code |
| negative guarantees | no partial write, no secret, no duplicate, no out-of-root file as applicable |

The required suites and parity gates are in [Test and Acceptance](11-Test-and-Acceptance-Plan.md#parity-gates).

## Defect and decision traceability

Every observed defect/ambiguity receives a stable `DEF-*` or `DEC-*` entry in [Known Defects and Open Decisions](14-Known-Defects-and-Open-Decisions.md#decision-register). A safe behavior is not considered approved merely because this wiki recommends it. Implementation/test references must name the decision and prove both:

- the selected production result; and
- the strict observed result when characterization is required and safe.

Security weaknesses are documented but never enabled through compatibility flags.

## Documentation validation gates

Before using this wiki as an implementation baseline:

1. regenerate the source inventory and require the expected 329 entries or review the snapshot change;
2. regenerate BMS catalog and require 21 sources/585 named fields for this snapshot;
3. check every relative Markdown link target exists;
4. check every `#Lx`/`#Lx-Ly` source anchor is within the linked file’s line count;
5. require all 44 COBOL paths and all 55 JCL paths to appear in the functional/program catalogs, not only the CSV;
6. validate Markdown table column counts and balanced code fences;
7. search for unresolved authoring tokens/placeholders and accidental generated-script literals;
8. recompute fixture oracles from raw fixtures/codecs independently of prose;
9. review all `Decision required` statements before freezing an implementation baseline.

Generated outputs must be reproducible and carry no manual edits that the generator would overwrite.

## Coverage limitations

“Complete” here means complete for the repository snapshot. It cannot prove behavior supplied only by the missing deployed mainframe environment, including absent datasets/members, DSNTIAC/TXT2PDF/site procedures, MQ definitions, the orphan `COCRDSEC`, production RACF policies, or runtime-specific defaults where source omits an option. These are explicitly listed in [Environment and External Dependencies](14-Known-Defects-and-Open-Decisions.md#environment-and-external-dependencies) and must be decided or recovered from the actual environment before claiming deployment parity.

---

[<- Operations](12-Operations-and-Deployment.md) | [Home](Home.md) | [Known defects ->](14-Known-Defects-and-Open-Decisions.md)
