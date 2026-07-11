# CardDemo .NET 10 console modernization documentation

This directory is a Markdown wiki specifying how to rebuild CardDemo as one .NET 10 (<code>net10.0</code>) console application. It documents the legacy product and proposed target; it does not claim that the target has been implemented.

Start at **[Home](Home.md)**. The wiki navigation is in **[_Sidebar.md](_Sidebar.md)** and the evidence/claim rules are in **[Documentation Conventions](Documentation-Conventions.md)**.

The verified snapshot contains **329 artifacts**, including **44 COBOL programs**, **55 JCL files**, **21 BMS sources with 585 named fields**, and **71 optional-module artifacts**. Counts and SHA-256 hashes are in the [Source Inventory](Appendix-Source-Inventory.md#inventory-snapshot); screen fields are in the [BMS Field Catalog](Appendix-BMS-Field-Catalog.md).

The documentation is source-traceable. Legacy behavior is not inferred from a proposed .NET design, and proposed design choices are not presented as COBOL facts. Use the [Glossary](Glossary.md) for terminology.

Validation and regeneration tools:

- [documentation validator](tools/Test-Documentation.ps1)
- [source-inventory generator](tools/New-SourceInventory.ps1)
- [BMS-field-catalog generator](tools/New-BmsFieldCatalog.ps1)
- [generated source inventory CSV](appendices/source-inventory.csv)
