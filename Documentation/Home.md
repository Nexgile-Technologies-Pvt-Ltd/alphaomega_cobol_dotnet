# CardDemo .NET 10 console rebuild wiki

[Product scope](01-Product-Scope.md) · [Architecture](02-System-Context-and-Architecture.md) · [Functional requirements](03-Functional-Requirements.md) · [Traceability](13-Traceability-and-Coverage.md)

## Purpose

This wiki is the source-traceable product specification for creating one .NET 10 (<code>net10.0</code>) console application that reproduces the behavior found under <code>Old_Cobol_Code</code>. It covers the base CICS/VSAM application, its batch estate, and the three optional modules shipped in the same source tree. It records source inconsistencies and incomplete legacy behavior so they cannot silently become invented requirements. It does **not** claim that the .NET application has already been implemented.

The source snapshot contains 329 artifacts: 240 under <code>app</code>, 56 under <code>scripts</code>, 15 under <code>samples</code>, 12 under <code>diagrams</code>, and 6 repository-level files. The reproducible inventory records every path, size, applicable line count, and SHA-256 hash in [Source Inventory](Appendix-Source-Inventory.md#inventory-snapshot).

| Verified snapshot measure | Count | Evidence |
|---|---:|---|
| All source artifacts | 329 | [generated hash ledger](appendices/source-inventory.csv) |
| COBOL programs | 44 | [inventory artifact counts](Appendix-Source-Inventory.md#artifact-counts) |
| JCL files | 55 | [inventory artifact counts](Appendix-Source-Inventory.md#artifact-counts) |
| BMS source maps / named fields | 21 / 585 | [BMS Field Catalog](Appendix-BMS-Field-Catalog.md) |
| Optional-module artifacts | 71 | [27 transaction-type + 40 authorization + 4 VSAM/MQ](07-Optional-Modules-and-Integrations.md#extension-artifact-coverage) |

## Product in one paragraph

CardDemo is a credit-card management demonstration system. Authenticated regular users can view and update account/card information, browse and add transactions, request a dated transaction report, and create bill-payment transactions. Administrators are routed to security-user maintenance and, when the optional Db2 module is installed, transaction-type maintenance. Batch processing loads and refreshes master data, posts daily transactions, rejects invalid transactions, calculates monthly interest, rebuilds transaction indexes, and generates text/HTML statements and fixed-width transaction reports. Optional modules add pending authorization processing through CICS, IMS, Db2 and MQ; Db2-backed transaction-type maintenance; and MQ request/reply services for system date and account inquiry. The top-level source description is at [`Old_Cobol_Code/README.md`](../Old_Cobol_Code/README.md).

## Wiki map

### Understand the legacy product

- [Product Scope and Capability Map](01-Product-Scope.md#capability-map)
- [System Context and Architecture](02-System-Context-and-Architecture.md#legacy-runtime-context)
- [Functional Requirements](03-Functional-Requirements.md#requirements-index)
- [Online Screens and Navigation](04-Online-Screens-and-Navigation.md#transaction-and-screen-catalog)
- [Batch Processing](05-Batch-Processing.md#batch-workload-catalog)
- [Domain Data Model](06-Domain-Data-Model.md#logical-relationship-model)
- [Optional Modules and Integrations](07-Optional-Modules-and-Integrations.md#module-boundaries)
- [Security and Controls](08-Security-and-Controls.md#legacy-security-model)

### Build the .NET replacement

- [.NET Target Architecture](09-DotNet-Target-Architecture.md#target-component-model)
- [Implementation Plan](10-Implementation-Plan.md#delivery-slices)
- [Test and Acceptance Plan](11-Test-and-Acceptance-Plan.md#parity-gates)
- [Operations and Deployment](12-Operations-and-Deployment.md#operational-workloads)

### Verify completeness and resolve risk

- [Traceability and Coverage](13-Traceability-and-Coverage.md#coverage-summary)
- [Known Defects, Ambiguities and Open Decisions](14-Known-Defects-and-Open-Decisions.md#decision-policy)
- [Program Catalog](Appendix-Program-Catalog.md#catalog)
- [BMS Field Catalog](Appendix-BMS-Field-Catalog.md)
- [File and Record Layouts](Appendix-File-and-Record-Layouts.md#layout-index)
- [Source Inventory](Appendix-Source-Inventory.md#inventory-snapshot)
- [Glossary](Glossary.md)

## Reading paths

| Reader | Recommended path |
|---|---|
| Product owner / analyst | [Scope](01-Product-Scope.md) → [Functional requirements](03-Functional-Requirements.md) → [Known decisions](14-Known-Defects-and-Open-Decisions.md) |
| .NET architect | [Architecture](02-System-Context-and-Architecture.md) → [Data model](06-Domain-Data-Model.md) → [.NET target](09-DotNet-Target-Architecture.md) |
| Developer | [Online](04-Online-Screens-and-Navigation.md) or [Batch](05-Batch-Processing.md) → [BMS fields](Appendix-BMS-Field-Catalog.md) and [data layouts](Appendix-File-and-Record-Layouts.md) → [Traceability](13-Traceability-and-Coverage.md) |
| Tester | [Functional requirements](03-Functional-Requirements.md) → [Test plan](11-Test-and-Acceptance-Plan.md#parity-gates) → [BMS fields](Appendix-BMS-Field-Catalog.md) and [known legacy quirks](14-Known-Defects-and-Open-Decisions.md#decision-register) |
| Operations | [Batch](05-Batch-Processing.md) → [Operations](12-Operations-and-Deployment.md) → [Integrations](07-Optional-Modules-and-Integrations.md) |

## Validation tooling

The documentation evidence can be regenerated and checked without treating generated output as a new business specification:

- [New-SourceInventory.ps1](tools/New-SourceInventory.ps1) regenerates the 329-row path/hash ledger at [source-inventory.csv](appendices/source-inventory.csv).
- [New-BmsFieldCatalog.ps1](tools/New-BmsFieldCatalog.ps1) regenerates the 21-map, 585-named-field [BMS Field Catalog](Appendix-BMS-Field-Catalog.md).
- [Test-Documentation.ps1](tools/Test-Documentation.ps1) checks required pages, local links, source line anchors, inventory/catalog counts, and prohibited placeholder text.

Tool success validates documentation structure and snapshot reconciliation; it is not evidence that the target application exists or passes its future product tests.

## Evidence and compatibility policy

Every normative legacy statement is tied to source evidence. Labels and rules are defined in [Documentation Conventions](Documentation-Conventions.md#claim-labels). In summary:

- **Observed** means directly implemented or declared in a shipped artifact.
- **Derived** means a deterministic interpretation of multiple observed artifacts; the cited evidence is included.
- **Target** means a .NET design recommendation, not a claim about the COBOL product.
- **Decision required** means the source is contradictory, incomplete, environment-specific, or intentionally leaves a function unimplemented.

The parity baseline is the executable source, not screenshots or prose when those disagree. Such disagreements are listed rather than silently reconciled.

---

[Documentation conventions →](Documentation-Conventions.md) · [Glossary](Glossary.md) · [Validation tool](tools/Test-Documentation.ps1)
