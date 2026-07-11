# Appendix: source inventory

[← Traceability](13-Traceability-and-Coverage.md) · [Home](Home.md) · [Program catalog →](Appendix-Program-Catalog.md)

## Inventory snapshot

The analyzed `Old_Cobol_Code` snapshot contains **329 files** and **8,127,700 bytes**. The inventory was generated on 2026-07-10 from the workspace copy. Each row in [`appendices/source-inventory.csv`](appendices/source-inventory.csv) records the path relative to `Old_Cobol_Code`, assigned analysis area, artifact kind, byte count, text line count when applicable, and SHA-256 content hash.

The hash ledger is the completeness baseline. A later source change requires regenerating the ledger and re-running the coverage review; otherwise a page can be internally correct but stale.

## Area counts

| Area | Files | Treatment in this wiki |
|---|---:|---|
| Core application | 169 | Full functional, data, online, batch, operations, and migration analysis |
| Optional authorization / IMS / Db2 / MQ | 40 | Full optional-module specification |
| Optional transaction-type / Db2 | 27 | Full optional-module specification |
| Optional VSAM / MQ | 4 | Full optional-service specification |
| Developer automation | 56 | Build/submission behavior; 45 zero-byte `scripts/markers` files are identified as markers, not product functions (the repository holds 48 zero-byte marker/placeholder files in total — the other three are `.gitkeep` placeholders in the core application) |
| Build samples | 15 | Compilation/deployment reference; runtime ZIP files are inventoried but not treated as source authority over COBOL |
| Reference diagrams | 12 | Corroborating product views, subordinate to executable source |
| Repository metadata | 6 | Product description, licensing, contribution guidance and ignore rules |

## Artifact counts

| Artifact kind | Count |
|---|---:|
| COBOL source | 44 |
| COBOL copybook or generated BMS copybook | 62 |
| BMS map source | 21 |
| JCL | 55 |
| Cataloged procedure | 6 |
| CICS resource definitions | 4 |
| Db2 DDL / declaration copybook | 6 / 3 |
| IMS DBD / PSB | 4 / 4 |
| Utility control statements | 8 |
| Assembler source / macro | 2 / 2 |
| Schedulers (Control-M / CA 7) | 1 / 1 |
| Shell / AWK / build template automation | 9 / 1 / 1 |
| ASCII text data or captured output | 10 |
| EBCDIC data / initialization data / other binary data | 12 / 1 / 1 |
| Reference PNG / draw.io source | 11 / 1 |
| Runtime archive | 2 |
| Repository documentation | 6 |
| Zero-byte marker or placeholder | 48 |
| Other | 3 |

## COBOL analysis volume

| Area | Programs | Physical source lines |
|---|---:|---:|
| Core application | 31 | 20,650 |
| Optional authorization / IMS / Db2 / MQ | 8 | 4,344 |
| Optional transaction-type / Db2 | 3 | 4,037 |
| Optional VSAM / MQ | 2 | 1,144 |
| **Total** | **44** | **30,175** |

Physical lines include comments, sequence fields and blank lines. They are an audit measure, not a complexity metric.

## Reproduce the inventory

From the repository root on PowerShell:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Documentation\tools\New-SourceInventory.ps1
```

Expected completion text for this snapshot is `Wrote 329 source artifacts`. Any other count or any changed SHA-256 value means the traceability baseline needs review.

## Coverage interpretation

Inventory does not mean every artifact defines unique product behavior:

- Generated `cpy-bms` files mirror BMS map layouts and are checked against their corresponding map/program but are not a separate feature.
- EBCDIC and ASCII data are fixtures and encoding examples; they do not independently define validation rules.
- Zero-byte files under `scripts/markers` are version-processing markers used by developer automation.
- `samples` contains compilation and utility examples, while `app` contains the runtime application definition.
- `app/jcl/CBADMCDJ.jcl` contains older/inconsistent resource names; the current comprehensive `app/csd/CARDDEMO.CSD` and shipped programs take precedence. The discrepancy is tracked in [Source contradictions](14-Known-Defects-and-Open-Decisions.md#source-contradictions).

See [Traceability and Coverage](13-Traceability-and-Coverage.md#artifact-coverage) for how these artifacts map to authored pages.

---

[← Traceability](13-Traceability-and-Coverage.md) · [Home](Home.md) · [Program catalog →](Appendix-Program-Catalog.md)
