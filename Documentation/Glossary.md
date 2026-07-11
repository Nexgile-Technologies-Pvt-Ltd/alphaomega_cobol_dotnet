# Glossary

[<- Known defects](14-Known-Defects-and-Open-Decisions.md) | [Home](Home.md) | [Source inventory](Appendix-Source-Inventory.md)

Terms use the meaning proved by this source snapshot. A target-only term is marked **Target**.

## Product and data terms

| Term | Meaning in this wiki |
|---|---|
| account | Eleven-character credit account record containing status, limits, dates, current balance, cycle accumulators, ZIP and disclosure-group fields. [Account layout](Appendix-File-and-Record-Layouts.md#account-record--300-bytes). |
| account current balance | Signed account balance mutated by posting, interest and bill payment. The source does not supply a general ledger or universally define debit/credit accounting semantics beyond each program’s arithmetic. |
| card | Sixteen-character card record tied to an account, with CVV, embossed name, expiration and status. [Core entities](06-Domain-Data-Model.md#core-entity-catalog). |
| card cross-reference / xref | Card-keyed record joining card number to customer ID and account ID; an alternate index supports account lookup. Transactions depend on this mutable relationship. |
| category balance / TCATBAL | Signed accumulated amount keyed by account + transaction type + transaction category. Daily posting creates/increments it. |
| current-cycle credit/debit | Account accumulators. Posting adds nonnegative amounts to cycle credit and adds negative amounts as negative values to cycle debit. The implemented credit-limit formula subtracts cycle debit, so the sign convention matters. [Daily posting](06-Domain-Data-Model.md#daily-posting). |
| customer | Nine-character customer identity with names, address/contact, SSN, government/EFT identifiers, DOB, FICO and primary-holder flag. |
| daily transaction | 350-byte input transaction consumed by the posting program. Its layout matches the transaction logical fields but is a separate stream contract. |
| disclosure group/rate | Annual percentage rate keyed by group + type + category. Interest retries a missing group-specific rate using group `DEFAULT`. |
| fixture | Supplied sample data used as deterministic input. Fixture counts/values are acceptance examples, not universal production limits or rules. |
| pending authorization | Optional IMS-root aggregate containing account summary plus authorization-history children, supplemented by a Db2 fraud row. |
| processing timestamp | Transaction timestamp assigned during processing; often a fixed 26-character legacy representation. Do not assume every source path fills every character identically. |
| reject | Posting/import input that cannot be applied. Posting’s 430-byte reject record preserves the original 350-byte transaction plus reason fields. |
| transaction | Sixteen-character-ID master event with type/category, source/description, signed amount, card, merchant data and origin/processing timestamps. |
| transaction category | Four-character code scoped by a two-character transaction type; category alone is not a key. |
| transaction type | Two-character reference code such as fixture values `01`-`07`; the optional Db2 module maintains types and categories. |
| user / security user | Eight-character user ID record with names, password and one-character role. Legacy storage is plaintext; target storage is hashed. |

## Mainframe/runtime terms

| Term | Meaning / replacement relevance |
|---|---|
| AIX / path | VSAM alternate index and its access path. The target represents these as relational indexes/queries, while preserving browse ordering where functional. |
| BMS | CICS Basic Mapping Support source describing 24x80 screen labels, fields, positions, widths and attributes. [BMS field catalog](Appendix-BMS-Field-Catalog.md#catalog). |
| CICS | Transaction runtime used by online programs for maps, files, queues, routing and syncpoints. It is not required by the target console. |
| COMMAREA | Fixed shared area passed among CICS programs. Its useful 160-byte routing/user/context content becomes a typed target `SessionContext`. |
| CSD | CICS system-definition commands declaring files, programs, transactions, queues and security/recovery attributes. |
| Db2 | Relational database used by optional transaction-type and authorization fraud functions. The target default store is relational SQLite, with external adapters decided separately. |
| DBD / PSB | IMS database description and program specification block defining hierarchy and permitted access. Used as evidence for pending-authorization persistence/access. |
| DD / DDNAME | JCL dataset/resource assignment consumed by a program. Target commands replace implicit DD binding with explicit validated options/configuration. |
| ESDS | Entry-sequenced VSAM dataset. Present mainly in examples/optional structures. |
| GDG | z/OS generation data group used for versioned inputs/outputs/backups. Target run/archive manifests replace implicit generation naming. |
| IDCAMS | z/OS utility used to define/delete/load/repro VSAM data. Target database/file commands replace these mechanics. |
| IMS | Hierarchical database/transaction integration used by the optional authorization package. |
| internal reader | JES input destination to which JCL is written for submission. The report screen writes through TDQ `JOBS`; target stores a structured request instead. |
| JCL | z/OS Job Control Language that allocates files, executes programs/utilities and sequences steps. Each shipped JCL is cataloged as product, support, stale, malformed or sample evidence. |
| JES | z/OS job execution/submission environment. Replaced by console process exit codes plus external scheduling. |
| KSDS | Key-sequenced VSAM dataset. Target maps keys and alternate access to relational constraints/indexes. |
| MQ / MQMD | IBM MQ and its message descriptor used for optional request/reply services. Queue names, correlation and delivery semantics are explicit adapter concerns. |
| pseudo-conversational | CICS pattern that sends a screen, returns, and re-enters a transaction with state on the next key action. Target uses an explicit in-process screen state machine. |
| RRDS | Relative-record VSAM dataset, present in support/demo assets rather than a core entity contract. |
| SYNCPOINT / rollback | CICS/IMS/Db2 unit-of-work commit or rollback. Source programs use these inconsistently; safe target use cases define atomic transactions/outbox behavior. |
| TDQ | CICS transient-data queue. `JOBS` is an extra-partition queue used to submit fixed 80-byte JCL records. |
| VSAM | z/OS indexed/sequential access method backing core files. Layout/key behavior is preserved; VSAM itself is not embedded in .NET. |
| XCTL | CICS transfer of control to another program without return. Target navigation changes an explicit route/controller. |

## Record and COBOL terms

| Term | Meaning |
|---|---|
| `COMP` | Binary COBOL numeric representation. Width/endianness is codec-specific; transfer sequence is explicitly big-endian in this snapshot’s mainframe record. |
| `COMP-3` | Packed decimal: two decimal digits per byte except the final sign nibble. Target codecs validate every digit/sign nibble. |
| copybook | Shared COBOL record/constant/code fragment included by programs. Generated BMS copybooks expose map fields to COBOL. |
| EBCDIC | Mainframe character encoding used by supplied binary fixtures. The target selects an explicit code page; it never relies on process default encoding. |
| filler | Unnamed reserved bytes. Filler may need preservation for byte-for-byte re-export/reject behavior even though it is not a domain field. |
| fixed-width/display numeric | Text bytes whose exact width, sign and leading zeroes are part of the contract. A numeric-looking identifier remains a string. |
| LRECL | Logical record length declared for a dataset/output. Conflicting JCL/program values are recorded as defects. |
| `NUMVAL-C` | COBOL conversion/validation facility accepting formatted numeric text. Target uses an explicitly tested compatible grammar for fields that call it. |
| overpunch | Signed display-numeric convention encoding the sign in the final digit character. ASCII and EBCDIC variants require explicit tables. |
| PIC | COBOL picture clause declaring display/numeric width/scale/representation. Physical layouts reproduce the source PIC rather than inferred fixture labels. |
| return code / condition code | Process/job status consumed by JCL/schedulers. Target exit codes distinguish usage, business rejects, unavailable state, fatal failure and cancellation. |

## Terminal terms

| Term | Meaning |
|---|---|
| AID | CICS attention-identifier representing Enter, Clear or a PF key. Target terminal converts physical/textual key input into an equivalent typed event. |
| DARK | BMS attribute that prevents password characters from displaying. Target masked input also prevents capture in logs/snapshots. |
| F/PF key | Function/program-function key shown by legacy maps. This wiki uses F3/PF3 interchangeably for the user action; target may provide `/pf3` accessibility aliases. |
| protected/unprotected field | Output-only versus editable map field. Runtime programs sometimes change attributes after fetch/validation. |
| TTY | Interactive terminal. `carddemo interactive` requires one; batch commands support redirected streams. |

## Target architecture and verification terms

| Term | Meaning |
|---|---|
| characterization test | Test that records/proves observed source behavior, including a defect, without implying that behavior is the production target. |
| compatibility policy/switch | Individually named strict behavior selected for a test/migration comparison. There is no blanket “enable all bugs” option. |
| decision register | Evidence-backed list of contradictions, defects and choices that source cannot safely resolve. [Decision register](14-Known-Defects-and-Open-Decisions.md#decision-register). |
| deep link | Relative Markdown link to a wiki heading or source file/line range, used to trace a claim. |
| inbox/outbox | **Target:** durable message-processing pattern that deduplicates consumed input and records outbound reply in the same state transaction before delivery. |
| optimistic concurrency | **Target:** compare version/snapshot before save and report a conflict instead of overwriting another update. |
| run ledger | **Target:** persisted command/run identity, input fingerprints, profile, checkpoints, counts, outcomes and output identities supporting audit/restart. |
| `Safe` profile | Default target behavior preserving intended capability/contracts while correcting security, atomicity, bounds and known control-flow defects. |
| `StrictLegacy` profile | Nondefault characterization profile enabling only named, safe-to-reproduce quirks. It never enables plaintext passwords or missing authorization. |
| source precedence | Rule that executable behavior outranks declarative assets, fixtures, automation and prose when they conflict, while the conflict remains documented. |
| virtual terminal | **Target:** deterministic in-memory 24x80 terminal used to test fields, attributes, cursor, keys and screen snapshots without a physical console. |

---

[<- Known defects](14-Known-Defects-and-Open-Decisions.md) | [Home](Home.md) | [Source inventory](Appendix-Source-Inventory.md)
