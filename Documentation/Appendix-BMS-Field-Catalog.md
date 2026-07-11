# Appendix: BMS named-field catalog

[Online screens](04-Online-Screens-and-Navigation.md) | [Home](Home.md) | [Program catalog](Appendix-Program-Catalog.md)

This generated Markdown page enumerates every **named DFHMDF** field in all shipped core and optional BMS maps. Unnamed literal fields remain visible in the linked BMS source and are rendered through the screen templates; they are not input/output data fields. Positions are one-based 3270 row/column coordinates.

Generated from 21 BMS files with 585 named fields by [tools/New-BmsFieldCatalog.ps1](tools/New-BmsFieldCatalog.ps1).

## Catalog

## COPAU00

Source: [app/app-authorization-ims-db2-mq/bms/COPAU00.bms](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms)

| Map | Field | Row | Column | Length | ATTRB | Color | Highlight | Initial | Source |
|---|---|---:|---:|---:|---|---|---|---|---|
| COPAU0A | TRNNAME | 1 | 7 | 4 | ASKIP,FSET,NORM | BLUE |  |  | [L34](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L34) |
| COPAU0A | TITLE01 | 1 | 21 | 40 | ASKIP,FSET,NORM | YELLOW |  |  | [L38](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L38) |
| COPAU0A | CURDATE | 1 | 71 | 8 | ASKIP,FSET,NORM | BLUE |  | mm/dd/yy | [L47](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L47) |
| COPAU0A | PGMNAME | 2 | 7 | 8 | ASKIP,FSET,NORM | BLUE |  |  | [L57](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L57) |
| COPAU0A | TITLE02 | 2 | 21 | 40 | ASKIP,FSET,NORM | YELLOW |  |  | [L61](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L61) |
| COPAU0A | CURTIME | 2 | 71 | 8 | ASKIP,FSET,NORM | BLUE |  | hh:mm:ss | [L70](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L70) |
| COPAU0A | ACCTID | 5 | 19 | 11 | FSET,NORM,UNPROT | GREEN | UNDERLINE |  | [L84](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L84) |
| COPAU0A | CNAME | 6 | 10 | 25 | ASKIP,NORM | BLUE |  |  | [L96](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L96) |
| COPAU0A | CUSTID | 6 | 58 | 9 | ASKIP,NORM | BLUE |  |  | [L103](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L103) |
| COPAU0A | ADDR001 | 7 | 10 | 25 | ASKIP,NORM | BLUE |  |  | [L107](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L107) |
| COPAU0A | ACCSTAT | 7 | 58 | 1 | ASKIP,NORM | BLUE |  |  | [L114](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L114) |
| COPAU0A | ADDR002 | 8 | 10 | 25 | ASKIP,NORM | BLUE |  |  | [L118](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L118) |
| COPAU0A | PHONE1 | 9 | 15 | 13 | ASKIP,NORM | BLUE |  |  | [L125](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L125) |
| COPAU0A | APPRCNT | 9 | 58 | 3 | ASKIP,NORM | BLUE |  |  | [L132](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L132) |
| COPAU0A | DECLCNT | 9 | 76 | 3 | ASKIP,NORM | BLUE |  |  | [L139](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L139) |
| COPAU0A | CREDLIM | 11 | 19 | 12 | ASKIP,FSET,NORM | BLUE |  |   | [L147](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L147) |
| COPAU0A | CASHLIM | 11 | 46 | 9 | ASKIP,FSET,NORM | BLUE |  |   | [L156](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L156) |
| COPAU0A | APPRAMT | 11 | 69 | 10 | ASKIP,FSET,NORM | BLUE |  |   | [L165](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L165) |
| COPAU0A | CREDBAL | 12 | 19 | 12 | ASKIP,FSET,NORM | BLUE |  |   | [L174](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L174) |
| COPAU0A | CASHBAL | 12 | 46 | 9 | ASKIP,FSET,NORM | BLUE |  |   | [L183](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L183) |
| COPAU0A | DECLAMT | 12 | 69 | 10 | ASKIP,FSET,NORM | BLUE |  |   | [L192](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L192) |
| COPAU0A | SEL0001 | 16 | 3 | 1 | FSET,NORM,UNPROT | GREEN | UNDERLINE |   | [L277](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L277) |
| COPAU0A | TRNID01 | 16 | 8 | 16 | ASKIP,FSET,NORM | BLUE |  |   | [L286](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L286) |
| COPAU0A | PDATE01 | 16 | 27 | 8 | ASKIP,FSET,NORM | BLUE |  |   | [L291](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L291) |
| COPAU0A | PTIME01 | 16 | 38 | 8 | ASKIP,FSET,NORM | BLUE |  |   | [L296](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L296) |
| COPAU0A | PTYPE01 | 16 | 49 | 4 | ASKIP,FSET,NORM | BLUE |  |   | [L301](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L301) |
| COPAU0A | PAPRV01 | 16 | 58 | 1 | ASKIP,FSET,NORM | BLUE |  |   | [L306](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L306) |
| COPAU0A | PSTAT01 | 16 | 63 | 1 | ASKIP,FSET,NORM | BLUE |  |   | [L311](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L311) |
| COPAU0A | PAMT001 | 16 | 67 | 12 | ASKIP,FSET,NORM | BLUE |  |   | [L316](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L316) |
| COPAU0A | SEL0002 | 17 | 3 | 1 | FSET,NORM,UNPROT | GREEN | UNDERLINE |   | [L321](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L321) |
| COPAU0A | TRNID02 | 17 | 8 | 16 | ASKIP,FSET,NORM | BLUE |  |   | [L330](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L330) |
| COPAU0A | PDATE02 | 17 | 27 | 8 | ASKIP,FSET,NORM | BLUE |  |   | [L335](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L335) |
| COPAU0A | PTIME02 | 17 | 38 | 8 | ASKIP,FSET,NORM | BLUE |  |   | [L340](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L340) |
| COPAU0A | PTYPE02 | 17 | 49 | 4 | ASKIP,FSET,NORM | BLUE |  |   | [L345](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L345) |
| COPAU0A | PAPRV02 | 17 | 58 | 1 | ASKIP,FSET,NORM | BLUE |  |   | [L350](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L350) |
| COPAU0A | PSTAT02 | 17 | 63 | 1 | ASKIP,FSET,NORM | BLUE |  |   | [L355](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L355) |
| COPAU0A | PAMT002 | 17 | 67 | 12 | ASKIP,FSET,NORM | BLUE |  |   | [L360](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L360) |
| COPAU0A | SEL0003 | 18 | 3 | 1 | FSET,NORM,UNPROT | GREEN | UNDERLINE |   | [L365](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L365) |
| COPAU0A | TRNID03 | 18 | 8 | 16 | ASKIP,FSET,NORM | BLUE |  |   | [L374](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L374) |
| COPAU0A | PDATE03 | 18 | 27 | 8 | ASKIP,FSET,NORM | BLUE |  |   | [L379](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L379) |
| COPAU0A | PTIME03 | 18 | 38 | 8 | ASKIP,FSET,NORM | BLUE |  |   | [L384](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L384) |
| COPAU0A | PTYPE03 | 18 | 49 | 4 | ASKIP,FSET,NORM | BLUE |  |   | [L389](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L389) |
| COPAU0A | PAPRV03 | 18 | 58 | 1 | ASKIP,FSET,NORM | BLUE |  |   | [L394](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L394) |
| COPAU0A | PSTAT03 | 18 | 63 | 1 | ASKIP,FSET,NORM | BLUE |  |   | [L399](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L399) |
| COPAU0A | PAMT003 | 18 | 67 | 12 | ASKIP,FSET,NORM | BLUE |  |   | [L404](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L404) |
| COPAU0A | SEL0004 | 19 | 3 | 1 | FSET,NORM,UNPROT | GREEN | UNDERLINE |   | [L409](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L409) |
| COPAU0A | TRNID04 | 19 | 8 | 16 | ASKIP,FSET,NORM | BLUE |  |   | [L418](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L418) |
| COPAU0A | PDATE04 | 19 | 27 | 8 | ASKIP,FSET,NORM | BLUE |  |   | [L423](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L423) |
| COPAU0A | PTIME04 | 19 | 38 | 8 | ASKIP,FSET,NORM | BLUE |  |   | [L428](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L428) |
| COPAU0A | PTYPE04 | 19 | 49 | 4 | ASKIP,FSET,NORM | BLUE |  |   | [L433](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L433) |
| COPAU0A | PAPRV04 | 19 | 58 | 1 | ASKIP,FSET,NORM | BLUE |  |   | [L438](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L438) |
| COPAU0A | PSTAT04 | 19 | 63 | 1 | ASKIP,FSET,NORM | BLUE |  |   | [L443](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L443) |
| COPAU0A | PAMT004 | 19 | 67 | 12 | ASKIP,FSET,NORM | BLUE |  |   | [L448](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L448) |
| COPAU0A | TRNID05 | 20 | 8 | 16 | ASKIP,FSET,NORM | BLUE |  |   | [L453](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L453) |
| COPAU0A | PDATE05 | 20 | 27 | 8 | ASKIP,FSET,NORM | BLUE |  |   | [L458](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L458) |
| COPAU0A | PTIME05 | 20 | 38 | 8 | ASKIP,FSET,NORM | BLUE |  |   | [L463](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L463) |
| COPAU0A | PTYPE05 | 20 | 49 | 4 | ASKIP,FSET,NORM | BLUE |  |   | [L468](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L468) |
| COPAU0A | PAPRV05 | 20 | 58 | 1 | ASKIP,FSET,NORM | BLUE |  |   | [L473](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L473) |
| COPAU0A | PSTAT05 | 20 | 63 | 1 | ASKIP,FSET,NORM | BLUE |  |   | [L478](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L478) |
| COPAU0A | PAMT005 | 20 | 67 | 12 | ASKIP,FSET,NORM | BLUE |  |   | [L483](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L483) |
| COPAU0A | SEL0005 | 20 | 3 | 1 | FSET,NORM,UNPROT | GREEN | UNDERLINE |   | [L488](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L488) |
| COPAU0A | ERRMSG | 23 | 1 | 78 | ASKIP,BRT,FSET | RED |  |  | [L503](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU00.bms#L503) |

## COPAU01

Source: [app/app-authorization-ims-db2-mq/bms/COPAU01.bms](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU01.bms)

| Map | Field | Row | Column | Length | ATTRB | Color | Highlight | Initial | Source |
|---|---|---:|---:|---:|---|---|---|---|---|
| COPAU1A | TRNNAME | 1 | 7 | 4 | ASKIP,NORM | BLUE |  |  | [L34](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU01.bms#L34) |
| COPAU1A | TITLE01 | 1 | 21 | 40 | ASKIP,NORM | YELLOW |  |  | [L38](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU01.bms#L38) |
| COPAU1A | CURDATE | 1 | 71 | 8 | ASKIP,NORM | BLUE |  | mm/dd/yy | [L47](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU01.bms#L47) |
| COPAU1A | PGMNAME | 2 | 7 | 8 | ASKIP,NORM | BLUE |  |  | [L57](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU01.bms#L57) |
| COPAU1A | TITLE02 | 2 | 21 | 40 | ASKIP,NORM | YELLOW |  |  | [L61](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU01.bms#L61) |
| COPAU1A | CURTIME | 2 | 71 | 8 | ASKIP,NORM | BLUE |  | hh:mm:ss | [L70](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU01.bms#L70) |
| COPAU1A | CARDNUM | 7 | 11 | 16 | ASKIP,NORM | PINK |  |  | [L85](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU01.bms#L85) |
| COPAU1A | AUTHDT | 7 | 43 | 10 | ASKIP,NORM | PINK |  |   | [L94](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU01.bms#L94) |
| COPAU1A | AUTHTM | 7 | 68 | 10 | ASKIP,NORM | PINK |  |   | [L104](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU01.bms#L104) |
| COPAU1A | AUTHRSP | 9 | 14 | 1 | ASKIP,NORM | PINK |  |   | [L114](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU01.bms#L114) |
| COPAU1A | AUTHRSN | 9 | 32 | 20 | ASKIP,NORM | BLUE |  |   | [L124](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU01.bms#L124) |
| COPAU1A | AUTHCD | 9 | 68 | 6 | ASKIP,NORM | BLUE |  |   | [L134](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU01.bms#L134) |
| COPAU1A | AUTHAMT | 11 | 11 | 12 | ASKIP,NORM | BLUE |  |   | [L144](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU01.bms#L144) |
| COPAU1A | POSEMD | 11 | 46 | 4 | ASKIP,NORM | BLUE |  |   | [L154](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU01.bms#L154) |
| COPAU1A | AUTHSRC | 11 | 68 | 10 | ASKIP,NORM | BLUE |  |   | [L164](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU01.bms#L164) |
| COPAU1A | MCCCD | 13 | 13 | 4 | ASKIP,NORM | BLUE |  |   | [L174](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU01.bms#L174) |
| COPAU1A | CRDEXP | 13 | 42 | 5 | ASKIP,NORM | BLUE |  |   | [L184](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU01.bms#L184) |
| COPAU1A | AUTHTYP | 13 | 64 | 14 | ASKIP,NORM | BLUE |  |   | [L194](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU01.bms#L194) |
| COPAU1A | TRNID | 15 | 12 | 15 | ASKIP,NORM | BLUE |  |   | [L204](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU01.bms#L204) |
| COPAU1A | AUTHMTC | 15 | 46 | 1 | ASKIP,NORM | RED |  |   | [L214](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU01.bms#L214) |
| COPAU1A | AUTHFRD | 15 | 67 | 10 | ASKIP,NORM | RED |  |   | [L224](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU01.bms#L224) |
| COPAU1A | MERNAME | 19 | 9 | 25 | ASKIP,NORM | BLUE |  |   | [L239](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU01.bms#L239) |
| COPAU1A | MERID | 19 | 55 | 15 | ASKIP,NORM | BLUE |  |   | [L249](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU01.bms#L249) |
| COPAU1A | MERCITY | 21 | 9 | 25 | ASKIP,NORM | BLUE |  |   | [L259](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU01.bms#L259) |
| COPAU1A | MERST | 21 | 49 | 2 | ASKIP,NORM | BLUE |  |   | [L269](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU01.bms#L269) |
| COPAU1A | MERZIP | 21 | 61 | 10 | ASKIP,NORM | BLUE |  |   | [L279](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU01.bms#L279) |
| COPAU1A | ERRMSG | 23 | 1 | 78 | ASKIP,BRT,FSET | RED |  |  | [L284](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/bms/COPAU01.bms#L284) |

## COTRTLI

Source: [app/app-transaction-type-db2/bms/COTRTLI.bms](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms)

| Map | Field | Row | Column | Length | ATTRB | Color | Highlight | Initial | Source |
|---|---|---:|---:|---:|---|---|---|---|---|
| CTRTLIA | TRNNAME | 1 | 7 | 4 | ASKIP,FSET,NORM | BLUE |  |  | [L34](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L34) |
| CTRTLIA | TITLE01 | 1 | 21 | 40 | ASKIP,NORM | YELLOW |  |  | [L38](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L38) |
| CTRTLIA | CURDATE | 1 | 71 | 8 | ASKIP,NORM | BLUE |  | mm/dd/yy | [L47](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L47) |
| CTRTLIA | PGMNAME | 2 | 7 | 8 | ASKIP,NORM | BLUE |  |  | [L57](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L57) |
| CTRTLIA | TITLE02 | 2 | 21 | 40 | ASKIP,NORM | YELLOW |  |  | [L61](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L61) |
| CTRTLIA | CURTIME | 2 | 71 | 8 | ASKIP,NORM | BLUE |  | hh:mm:ss | [L70](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L70) |
| CTRTLIA | PAGENO | 4 | 76 | 3 |  |  |  |  | [L82](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L82) |
| CTRTLIA | TRTYPE | 6 | 44 | 2 | FSET,IC,NORM,UNPROT | GREEN | UNDERLINE |  | [L89](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L89) |
| CTRTLIA | TRDESC | 8 | 25 | 50 | FSET,NORM,UNPROT | GREEN | UNDERLINE |  | [L101](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L101) |
| CTRTLIA | TRTSEL1 | 12 | 6 | 1 | FSET,NORM,PROT | DEFAULT | UNDERLINE |  | [L133](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L133) |
| CTRTLIA | TRTTYP1 | 12 | 17 | 2 | FSET,NORM,PROT | DEFAULT | OFF |  | [L140](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L140) |
| CTRTLIA | TRTYPD1 | 12 | 25 | 50 | FSET,NORM,UNPROT | DEFAULT | OFF |  | [L147](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L147) |
| CTRTLIA | TRTSEL2 | 13 | 6 | 1 | FSET,NORM,PROT | DEFAULT | UNDERLINE |  | [L154](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L154) |
| CTRTLIA | TRTTYP2 | 13 | 17 | 2 | FSET,NORM,PROT | DEFAULT | OFF |  | [L161](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L161) |
| CTRTLIA | TRTYPD2 | 13 | 25 | 50 | FSET,NORM,UNPROT | DEFAULT | OFF |  | [L168](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L168) |
| CTRTLIA | TRTSEL3 | 14 | 6 | 1 | FSET,NORM,PROT | DEFAULT | UNDERLINE |  | [L175](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L175) |
| CTRTLIA | TRTTYP3 | 14 | 17 | 2 | FSET,NORM,PROT | DEFAULT | OFF |  | [L182](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L182) |
| CTRTLIA | TRTYPD3 | 14 | 25 | 50 | FSET,NORM,UNPROT | DEFAULT | OFF |  | [L189](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L189) |
| CTRTLIA | TRTSEL4 | 15 | 6 | 1 | FSET,NORM,PROT | DEFAULT | UNDERLINE |  | [L196](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L196) |
| CTRTLIA | TRTTYP4 | 15 | 17 | 2 | FSET,NORM,PROT | DEFAULT | OFF |  | [L203](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L203) |
| CTRTLIA | TRTYPD4 | 15 | 25 | 50 | FSET,NORM,UNPROT | DEFAULT | OFF |  | [L210](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L210) |
| CTRTLIA | TRTSEL5 | 16 | 6 | 1 | FSET,NORM,PROT | DEFAULT | UNDERLINE |  | [L217](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L217) |
| CTRTLIA | TRTTYP5 | 16 | 17 | 2 | FSET,NORM,PROT | DEFAULT | OFF |  | [L224](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L224) |
| CTRTLIA | TRTYPD5 | 16 | 25 | 50 | FSET,NORM,UNPROT | DEFAULT | OFF |  | [L231](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L231) |
| CTRTLIA | TRTSEL6 | 17 | 6 | 1 | FSET,NORM,PROT | DEFAULT | UNDERLINE |  | [L238](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L238) |
| CTRTLIA | TRTTYP6 | 17 | 17 | 2 | FSET,NORM,PROT | DEFAULT | OFF |  | [L245](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L245) |
| CTRTLIA | TRTYPD6 | 17 | 25 | 50 | FSET,NORM,UNPROT | DEFAULT | OFF |  | [L252](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L252) |
| CTRTLIA | TRTSEL7 | 18 | 6 | 1 | FSET,NORM,PROT | DEFAULT | UNDERLINE |  | [L259](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L259) |
| CTRTLIA | TRTTYP7 | 18 | 17 | 2 | FSET,NORM,PROT | DEFAULT | OFF |  | [L266](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L266) |
| CTRTLIA | TRTYPD7 | 18 | 25 | 50 | FSET,NORM,UNPROT | DEFAULT | OFF |  | [L273](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L273) |
| CTRTLIA | TRTSELA | 19 | 6 | 1 | FSET,NORM,PROT | DEFAULT | OFF |  | [L280](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L280) |
| CTRTLIA | TRTTYPA | 19 | 17 | 2 | FSET,NORM,PROT | DEFAULT | OFF |  | [L287](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L287) |
| CTRTLIA | TRTDSCA | 19 | 25 | 50 | FSET,NORM,PROT | DEFAULT | OFF |  | [L294](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L294) |
| CTRTLIA | INFOMSG | 21 | 19 | 45 | PROT | NEUTRAL | OFF |  | [L301](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L301) |
| CTRTLIA | ERRMSG | 23 | 1 | 78 | ASKIP,BRT,FSET | RED |  |  | [L308](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L308) |
| CTRTLIA | BUTNF02 | 24 | 1 | 7 | ASKIP,NORM | TURQUOISE |  | F2=Add | [L312](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L312) |
| CTRTLIA | BUTNF03 | 24 | 10 | 7 | ASKIP,NORM | TURQUOISE |  | F3=Exit | [L317](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L317) |
| CTRTLIA | BUTNF07 | 24 | 19 | 10 | ASKIP,NORM | TURQUOISE |  | F7=Page Up | [L322](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L322) |
| CTRTLIA | BUTNF08 | 24 | 32 | 10 | ASKIP,NORM | TURQUOISE |  | F8=Page Dn | [L327](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L327) |
| CTRTLIA | BUTNF10 | 24 | 44 | 8 | ASKIP,NORM | TURQUOISE |  | F10=Save | [L332](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTLI.bms#L332) |

## COTRTUP

Source: [app/app-transaction-type-db2/bms/COTRTUP.bms](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTUP.bms)

| Map | Field | Row | Column | Length | ATTRB | Color | Highlight | Initial | Source |
|---|---|---:|---:|---:|---|---|---|---|---|
| CTRTUPA | TRNNAME | 1 | 7 | 4 | ASKIP,FSET,NORM | BLUE |  |  | [L34](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTUP.bms#L34) |
| CTRTUPA | TITLE01 | 1 | 21 | 40 | ASKIP,NORM | YELLOW |  |  | [L38](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTUP.bms#L38) |
| CTRTUPA | CURDATE | 1 | 71 | 8 | ASKIP,NORM | BLUE |  | mm/dd/yy | [L47](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTUP.bms#L47) |
| CTRTUPA | PGMNAME | 2 | 7 | 8 | ASKIP,NORM | BLUE |  |  | [L57](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTUP.bms#L57) |
| CTRTUPA | TITLE02 | 2 | 21 | 40 | ASKIP,NORM | YELLOW |  |  | [L61](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTUP.bms#L61) |
| CTRTUPA | CURTIME | 2 | 71 | 8 | ASKIP,NORM | BLUE |  | hh:mm:ss | [L70](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTUP.bms#L70) |
| CTRTUPA | TRTYPCD | 12 | 26 | 2 | IC,UNPROT |  | UNDERLINE |  | [L84](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTUP.bms#L84) |
| CTRTUPA | TRTYDSC | 14 | 26 | 50 | UNPROT |  | UNDERLINE |  | [L94](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTUP.bms#L94) |
| CTRTUPA | INFOMSG | 22 | 23 | 45 | ASKIP | NEUTRAL | OFF |  | [L100](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTUP.bms#L100) |
| CTRTUPA | ERRMSG | 23 | 1 | 78 | ASKIP,BRT,FSET | RED |  |  | [L107](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTUP.bms#L107) |
| CTRTUPA | FKEYS | 24 | 1 | 21 | ASKIP,NORM | YELLOW |  | ENTER=Process F3=Exit | [L111](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTUP.bms#L111) |
| CTRTUPA | FKEY04 | 24 | 23 | 9 | ASKIP,DRK | YELLOW |  | F4=Delete | [L116](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTUP.bms#L116) |
| CTRTUPA | FKEY05 | 24 | 33 | 8 | ASKIP,DRK | YELLOW |  | F5=Save | [L121](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTUP.bms#L121) |
| CTRTUPA | FKEY06 | 24 | 43 | 6 | ASKIP,DRK | YELLOW |  | F6=Add | [L126](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTUP.bms#L126) |
| CTRTUPA | FKEY12 | 24 | 69 | 10 | ASKIP,DRK | YELLOW |  | F12=Cancel | [L131](../Old_Cobol_Code/app/app-transaction-type-db2/bms/COTRTUP.bms#L131) |

## COACTUP

Source: [app/bms/COACTUP.bms](../Old_Cobol_Code/app/bms/COACTUP.bms)

| Map | Field | Row | Column | Length | ATTRB | Color | Highlight | Initial | Source |
|---|---|---:|---:|---:|---|---|---|---|---|
| CACTUPA | TRNNAME | 1 | 7 | 4 | ASKIP,FSET,NORM | BLUE |  |  | [L34](../Old_Cobol_Code/app/bms/COACTUP.bms#L34) |
| CACTUPA | TITLE01 | 1 | 21 | 40 | ASKIP,NORM | YELLOW |  |  | [L38](../Old_Cobol_Code/app/bms/COACTUP.bms#L38) |
| CACTUPA | CURDATE | 1 | 71 | 8 | ASKIP,NORM | BLUE |  | mm/dd/yy | [L47](../Old_Cobol_Code/app/bms/COACTUP.bms#L47) |
| CACTUPA | PGMNAME | 2 | 7 | 8 | ASKIP,NORM | BLUE |  |  | [L57](../Old_Cobol_Code/app/bms/COACTUP.bms#L57) |
| CACTUPA | TITLE02 | 2 | 21 | 40 | ASKIP,NORM | YELLOW |  |  | [L61](../Old_Cobol_Code/app/bms/COACTUP.bms#L61) |
| CACTUPA | CURTIME | 2 | 71 | 8 | ASKIP,NORM | BLUE |  | hh:mm:ss | [L70](../Old_Cobol_Code/app/bms/COACTUP.bms#L70) |
| CACTUPA | ACCTSID | 5 | 38 | 11 | IC,UNPROT |  | UNDERLINE |  | [L84](../Old_Cobol_Code/app/bms/COACTUP.bms#L84) |
| CACTUPA | ACSTTUS | 5 | 70 | 1 | UNPROT |  | UNDERLINE |  | [L94](../Old_Cobol_Code/app/bms/COACTUP.bms#L94) |
| CACTUPA | OPNYEAR | 6 | 17 | 4 | FSET,UNPROT |  | UNDERLINE |  | [L104](../Old_Cobol_Code/app/bms/COACTUP.bms#L104) |
| CACTUPA | OPNMON | 6 | 24 | 2 | UNPROT |  | UNDERLINE |  | [L112](../Old_Cobol_Code/app/bms/COACTUP.bms#L112) |
| CACTUPA | OPNDAY | 6 | 29 | 2 | UNPROT |  | UNDERLINE |  | [L120](../Old_Cobol_Code/app/bms/COACTUP.bms#L120) |
| CACTUPA | ACRDLIM | 6 | 61 | 15 | FSET,UNPROT |  | UNDERLINE |  | [L132](../Old_Cobol_Code/app/bms/COACTUP.bms#L132) |
| CACTUPA | EXPYEAR | 7 | 17 | 4 | UNPROT |  | UNDERLINE |  | [L142](../Old_Cobol_Code/app/bms/COACTUP.bms#L142) |
| CACTUPA | EXPMON | 7 | 24 | 2 | UNPROT |  | UNDERLINE |  | [L150](../Old_Cobol_Code/app/bms/COACTUP.bms#L150) |
| CACTUPA | EXPDAY | 7 | 29 | 2 | UNPROT |  | UNDERLINE |  | [L158](../Old_Cobol_Code/app/bms/COACTUP.bms#L158) |
| CACTUPA | ACSHLIM | 7 | 61 | 15 | FSET,UNPROT |  | UNDERLINE |  | [L170](../Old_Cobol_Code/app/bms/COACTUP.bms#L170) |
| CACTUPA | RISYEAR | 8 | 17 | 4 | UNPROT |  | UNDERLINE |  | [L180](../Old_Cobol_Code/app/bms/COACTUP.bms#L180) |
| CACTUPA | RISMON | 8 | 24 | 2 | UNPROT |  | UNDERLINE |  | [L188](../Old_Cobol_Code/app/bms/COACTUP.bms#L188) |
| CACTUPA | RISDAY | 8 | 29 | 2 | UNPROT |  | UNDERLINE |  | [L196](../Old_Cobol_Code/app/bms/COACTUP.bms#L196) |
| CACTUPA | ACURBAL | 8 | 61 | 15 | FSET,UNPROT |  | UNDERLINE |  | [L208](../Old_Cobol_Code/app/bms/COACTUP.bms#L208) |
| CACTUPA | ACRCYCR | 9 | 61 | 15 | FSET,UNPROT |  | UNDERLINE |  | [L219](../Old_Cobol_Code/app/bms/COACTUP.bms#L219) |
| CACTUPA | AADDGRP | 10 | 23 | 10 | UNPROT |  | UNDERLINE |  | [L229](../Old_Cobol_Code/app/bms/COACTUP.bms#L229) |
| CACTUPA | ACRCYDB | 10 | 61 | 15 | FSET,UNPROT |  | UNDERLINE |  | [L240](../Old_Cobol_Code/app/bms/COACTUP.bms#L240) |
| CACTUPA | ACSTNUM | 12 | 23 | 9 | UNPROT |  | UNDERLINE |  | [L254](../Old_Cobol_Code/app/bms/COACTUP.bms#L254) |
| CACTUPA | ACTSSN1 | 12 | 55 | 3 | UNPROT |  | UNDERLINE | 999 | [L264](../Old_Cobol_Code/app/bms/COACTUP.bms#L264) |
| CACTUPA | ACTSSN2 | 12 | 61 | 2 | UNPROT |  | UNDERLINE | 99 | [L272](../Old_Cobol_Code/app/bms/COACTUP.bms#L272) |
| CACTUPA | ACTSSN3 | 12 | 66 | 4 | UNPROT |  | UNDERLINE | 9999 | [L280](../Old_Cobol_Code/app/bms/COACTUP.bms#L280) |
| CACTUPA | DOBYEAR | 13 | 23 | 4 | UNPROT |  | UNDERLINE |  | [L291](../Old_Cobol_Code/app/bms/COACTUP.bms#L291) |
| CACTUPA | DOBMON | 13 | 30 | 2 | UNPROT |  | UNDERLINE |  | [L299](../Old_Cobol_Code/app/bms/COACTUP.bms#L299) |
| CACTUPA | DOBDAY | 13 | 35 | 2 | UNPROT |  | UNDERLINE |  | [L307](../Old_Cobol_Code/app/bms/COACTUP.bms#L307) |
| CACTUPA | ACSTFCO | 13 | 62 | 3 | UNPROT |  | UNDERLINE |  | [L318](../Old_Cobol_Code/app/bms/COACTUP.bms#L318) |
| CACTUPA | ACSFNAM | 15 | 1 | 25 | UNPROT |  | UNDERLINE |  | [L336](../Old_Cobol_Code/app/bms/COACTUP.bms#L336) |
| CACTUPA | ACSMNAM | 15 | 28 | 25 | UNPROT |  | UNDERLINE |  | [L342](../Old_Cobol_Code/app/bms/COACTUP.bms#L342) |
| CACTUPA | ACSLNAM | 15 | 55 | 25 | UNPROT |  | UNDERLINE |  | [L348](../Old_Cobol_Code/app/bms/COACTUP.bms#L348) |
| CACTUPA | ACSADL1 | 16 | 10 | 50 | UNPROT |  | UNDERLINE |  | [L356](../Old_Cobol_Code/app/bms/COACTUP.bms#L356) |
| CACTUPA | ACSSTTE | 16 | 73 | 2 | UNPROT |  | UNDERLINE |  | [L366](../Old_Cobol_Code/app/bms/COACTUP.bms#L366) |
| CACTUPA | ACSADL2 | 17 | 10 | 50 | UNPROT |  | UNDERLINE |  | [L372](../Old_Cobol_Code/app/bms/COACTUP.bms#L372) |
| CACTUPA | ACSZIPC | 17 | 73 | 5 | UNPROT |  | UNDERLINE |  | [L382](../Old_Cobol_Code/app/bms/COACTUP.bms#L382) |
| CACTUPA | ACSCITY | 18 | 10 | 50 | UNPROT |  | UNDERLINE |  | [L392](../Old_Cobol_Code/app/bms/COACTUP.bms#L392) |
| CACTUPA | ACSCTRY | 18 | 73 | 3 | UNPROT |  | UNDERLINE |  | [L402](../Old_Cobol_Code/app/bms/COACTUP.bms#L402) |
| CACTUPA | ACSPH1A | 19 | 10 | 3 | UNPROT |  | UNDERLINE |  | [L412](../Old_Cobol_Code/app/bms/COACTUP.bms#L412) |
| CACTUPA | ACSPH1B | 19 | 14 | 3 | UNPROT |  | UNDERLINE |  | [L417](../Old_Cobol_Code/app/bms/COACTUP.bms#L417) |
| CACTUPA | ACSPH1C | 19 | 18 | 4 | UNPROT |  | UNDERLINE |  | [L422](../Old_Cobol_Code/app/bms/COACTUP.bms#L422) |
| CACTUPA | ACSGOVT | 19 | 58 | 20 | UNPROT |  | UNDERLINE |  | [L433](../Old_Cobol_Code/app/bms/COACTUP.bms#L433) |
| CACTUPA | ACSPH2A | 20 | 10 | 3 | UNPROT |  | UNDERLINE |  | [L443](../Old_Cobol_Code/app/bms/COACTUP.bms#L443) |
| CACTUPA | ACSPH2B | 20 | 14 | 3 | UNPROT |  | UNDERLINE |  | [L448](../Old_Cobol_Code/app/bms/COACTUP.bms#L448) |
| CACTUPA | ACSPH2C | 20 | 18 | 4 | UNPROT |  | UNDERLINE |  | [L453](../Old_Cobol_Code/app/bms/COACTUP.bms#L453) |
| CACTUPA | ACSEFTC | 20 | 41 | 10 | UNPROT |  | UNDERLINE |  | [L464](../Old_Cobol_Code/app/bms/COACTUP.bms#L464) |
| CACTUPA | ACSPFLG | 20 | 78 | 1 | UNPROT |  | UNDERLINE |  | [L474](../Old_Cobol_Code/app/bms/COACTUP.bms#L474) |
| CACTUPA | INFOMSG | 22 | 23 | 45 | ASKIP | NEUTRAL | OFF |  | [L480](../Old_Cobol_Code/app/bms/COACTUP.bms#L480) |
| CACTUPA | ERRMSG | 23 | 1 | 78 | ASKIP,BRT,FSET | RED |  |  | [L489](../Old_Cobol_Code/app/bms/COACTUP.bms#L489) |
| CACTUPA | FKEYS | 24 | 1 | 21 | ASKIP,NORM | YELLOW |  | ENTER=Process F3=Exit | [L493](../Old_Cobol_Code/app/bms/COACTUP.bms#L493) |
| CACTUPA | FKEY05 | 24 | 23 | 7 | ASKIP,DRK | YELLOW |  | F5=Save | [L498](../Old_Cobol_Code/app/bms/COACTUP.bms#L498) |
| CACTUPA | FKEY12 | 24 | 31 | 10 | ASKIP,DRK | YELLOW |  | F12=Cancel | [L503](../Old_Cobol_Code/app/bms/COACTUP.bms#L503) |

## COACTVW

Source: [app/bms/COACTVW.bms](../Old_Cobol_Code/app/bms/COACTVW.bms)

| Map | Field | Row | Column | Length | ATTRB | Color | Highlight | Initial | Source |
|---|---|---:|---:|---:|---|---|---|---|---|
| CACTVWA | TRNNAME | 1 | 7 | 4 | ASKIP,FSET,NORM | BLUE |  |  | [L34](../Old_Cobol_Code/app/bms/COACTVW.bms#L34) |
| CACTVWA | TITLE01 | 1 | 21 | 40 | ASKIP,NORM | YELLOW |  |  | [L38](../Old_Cobol_Code/app/bms/COACTVW.bms#L38) |
| CACTVWA | CURDATE | 1 | 71 | 8 | ASKIP,NORM | BLUE |  | mm/dd/yy | [L47](../Old_Cobol_Code/app/bms/COACTVW.bms#L47) |
| CACTVWA | PGMNAME | 2 | 7 | 8 | ASKIP,NORM | BLUE |  |  | [L57](../Old_Cobol_Code/app/bms/COACTVW.bms#L57) |
| CACTVWA | TITLE02 | 2 | 21 | 40 | ASKIP,NORM | YELLOW |  |  | [L61](../Old_Cobol_Code/app/bms/COACTVW.bms#L61) |
| CACTVWA | CURTIME | 2 | 71 | 8 | ASKIP,NORM | BLUE |  | hh:mm:ss | [L70](../Old_Cobol_Code/app/bms/COACTVW.bms#L70) |
| CACTVWA | ACCTSID | 5 | 38 | 11 | FSET,IC,NORM,UNPROT | GREEN | UNDERLINE |  | [L84](../Old_Cobol_Code/app/bms/COACTVW.bms#L84) |
| CACTVWA | ACSTTUS | 5 | 70 | 1 | ASKIP |  | UNDERLINE |  | [L97](../Old_Cobol_Code/app/bms/COACTVW.bms#L97) |
| CACTVWA | ADTOPEN | 6 | 17 | 10 |  |  | UNDERLINE |  | [L107](../Old_Cobol_Code/app/bms/COACTVW.bms#L107) |
| CACTVWA | ACRDLIM | 6 | 61 | 15 |  |  | UNDERLINE |  | [L117](../Old_Cobol_Code/app/bms/COACTVW.bms#L117) |
| CACTVWA | AEXPDT | 7 | 17 | 10 |  |  | UNDERLINE |  | [L128](../Old_Cobol_Code/app/bms/COACTVW.bms#L128) |
| CACTVWA | ACSHLIM | 7 | 61 | 15 |  |  | UNDERLINE |  | [L138](../Old_Cobol_Code/app/bms/COACTVW.bms#L138) |
| CACTVWA | AREISDT | 8 | 17 | 10 |  |  | UNDERLINE |  | [L149](../Old_Cobol_Code/app/bms/COACTVW.bms#L149) |
| CACTVWA | ACURBAL | 8 | 61 | 15 |  |  | UNDERLINE |  | [L159](../Old_Cobol_Code/app/bms/COACTVW.bms#L159) |
| CACTVWA | ACRCYCR | 9 | 61 | 15 |  |  | UNDERLINE |  | [L171](../Old_Cobol_Code/app/bms/COACTVW.bms#L171) |
| CACTVWA | AADDGRP | 10 | 23 | 10 |  |  | UNDERLINE |  | [L182](../Old_Cobol_Code/app/bms/COACTVW.bms#L182) |
| CACTVWA | ACRCYDB | 10 | 61 | 15 |  |  | UNDERLINE |  | [L192](../Old_Cobol_Code/app/bms/COACTVW.bms#L192) |
| CACTVWA | ACSTNUM | 12 | 23 | 9 |  |  | UNDERLINE |  | [L207](../Old_Cobol_Code/app/bms/COACTVW.bms#L207) |
| CACTVWA | ACSTSSN | 12 | 54 | 12 |  |  | UNDERLINE |  | [L216](../Old_Cobol_Code/app/bms/COACTVW.bms#L216) |
| CACTVWA | ACSTDOB | 13 | 23 | 10 |  |  | UNDERLINE |  | [L225](../Old_Cobol_Code/app/bms/COACTVW.bms#L225) |
| CACTVWA | ACSTFCO | 13 | 61 | 3 |  |  | UNDERLINE |  | [L234](../Old_Cobol_Code/app/bms/COACTVW.bms#L234) |
| CACTVWA | ACSFNAM | 15 | 1 | 25 |  |  | UNDERLINE |  | [L251](../Old_Cobol_Code/app/bms/COACTVW.bms#L251) |
| CACTVWA | ACSMNAM | 15 | 28 | 25 |  |  | UNDERLINE |  | [L256](../Old_Cobol_Code/app/bms/COACTVW.bms#L256) |
| CACTVWA | ACSLNAM | 15 | 55 | 25 |  |  | UNDERLINE |  | [L261](../Old_Cobol_Code/app/bms/COACTVW.bms#L261) |
| CACTVWA | ACSADL1 | 16 | 10 | 50 |  |  | UNDERLINE |  | [L268](../Old_Cobol_Code/app/bms/COACTVW.bms#L268) |
| CACTVWA | ACSSTTE | 16 | 73 | 2 |  |  | UNDERLINE |  | [L277](../Old_Cobol_Code/app/bms/COACTVW.bms#L277) |
| CACTVWA | ACSADL2 | 17 | 10 | 50 |  |  | UNDERLINE |  | [L282](../Old_Cobol_Code/app/bms/COACTVW.bms#L282) |
| CACTVWA | ACSZIPC | 17 | 73 | 5 |  |  | UNDERLINE |  | [L291](../Old_Cobol_Code/app/bms/COACTVW.bms#L291) |
| CACTVWA | ACSCITY | 18 | 10 | 50 |  |  | UNDERLINE |  | [L301](../Old_Cobol_Code/app/bms/COACTVW.bms#L301) |
| CACTVWA | ACSCTRY | 18 | 73 | 3 |  |  | UNDERLINE |  | [L310](../Old_Cobol_Code/app/bms/COACTVW.bms#L310) |
| CACTVWA | ACSPHN1 | 19 | 10 | 13 |  |  | UNDERLINE |  | [L319](../Old_Cobol_Code/app/bms/COACTVW.bms#L319) |
| CACTVWA | ACSGOVT | 19 | 58 | 20 |  |  | UNDERLINE |  | [L326](../Old_Cobol_Code/app/bms/COACTVW.bms#L326) |
| CACTVWA | ACSPHN2 | 20 | 10 | 13 |  |  | UNDERLINE |  | [L335](../Old_Cobol_Code/app/bms/COACTVW.bms#L335) |
| CACTVWA | ACSEFTC | 20 | 41 | 10 |  |  | UNDERLINE |  | [L342](../Old_Cobol_Code/app/bms/COACTVW.bms#L342) |
| CACTVWA | ACSPFLG | 20 | 78 | 1 |  |  | UNDERLINE |  | [L351](../Old_Cobol_Code/app/bms/COACTVW.bms#L351) |
| CACTVWA | INFOMSG | 22 | 23 | 45 | PROT | NEUTRAL | OFF |  | [L356](../Old_Cobol_Code/app/bms/COACTVW.bms#L356) |
| CACTVWA | ERRMSG | 23 | 1 | 78 | ASKIP,BRT,FSET | RED |  |  | [L365](../Old_Cobol_Code/app/bms/COACTVW.bms#L365) |

## COADM01

Source: [app/bms/COADM01.bms](../Old_Cobol_Code/app/bms/COADM01.bms)

| Map | Field | Row | Column | Length | ATTRB | Color | Highlight | Initial | Source |
|---|---|---:|---:|---:|---|---|---|---|---|
| COADM1A | TRNNAME | 1 | 7 | 4 | ASKIP,FSET,NORM | BLUE |  |  | [L34](../Old_Cobol_Code/app/bms/COADM01.bms#L34) |
| COADM1A | TITLE01 | 1 | 21 | 40 | ASKIP,FSET,NORM | YELLOW |  |  | [L38](../Old_Cobol_Code/app/bms/COADM01.bms#L38) |
| COADM1A | CURDATE | 1 | 71 | 8 | ASKIP,FSET,NORM | BLUE |  | mm/dd/yy | [L47](../Old_Cobol_Code/app/bms/COADM01.bms#L47) |
| COADM1A | PGMNAME | 2 | 7 | 8 | ASKIP,FSET,NORM | BLUE |  |  | [L57](../Old_Cobol_Code/app/bms/COADM01.bms#L57) |
| COADM1A | TITLE02 | 2 | 21 | 40 | ASKIP,FSET,NORM | YELLOW |  |  | [L61](../Old_Cobol_Code/app/bms/COADM01.bms#L61) |
| COADM1A | CURTIME | 2 | 71 | 8 | ASKIP,FSET,NORM | BLUE |  | hh:mm:ss | [L70](../Old_Cobol_Code/app/bms/COADM01.bms#L70) |
| COADM1A | OPTN001 | 6 | 20 | 40 | ASKIP,FSET,NORM | BLUE |  |   | [L80](../Old_Cobol_Code/app/bms/COADM01.bms#L80) |
| COADM1A | OPTN002 | 7 | 20 | 40 | ASKIP,FSET,NORM | BLUE |  |   | [L85](../Old_Cobol_Code/app/bms/COADM01.bms#L85) |
| COADM1A | OPTN003 | 8 | 20 | 40 | ASKIP,FSET,NORM | BLUE |  |   | [L90](../Old_Cobol_Code/app/bms/COADM01.bms#L90) |
| COADM1A | OPTN004 | 9 | 20 | 40 | ASKIP,FSET,NORM | BLUE |  |   | [L95](../Old_Cobol_Code/app/bms/COADM01.bms#L95) |
| COADM1A | OPTN005 | 10 | 20 | 40 | ASKIP,FSET,NORM | BLUE |  |   | [L100](../Old_Cobol_Code/app/bms/COADM01.bms#L100) |
| COADM1A | OPTN006 | 11 | 20 | 40 | ASKIP,FSET,NORM | BLUE |  |   | [L105](../Old_Cobol_Code/app/bms/COADM01.bms#L105) |
| COADM1A | OPTN007 | 12 | 20 | 40 | ASKIP,FSET,NORM | BLUE |  |   | [L110](../Old_Cobol_Code/app/bms/COADM01.bms#L110) |
| COADM1A | OPTN008 | 13 | 20 | 40 | ASKIP,FSET,NORM | BLUE |  |   | [L115](../Old_Cobol_Code/app/bms/COADM01.bms#L115) |
| COADM1A | OPTN009 | 14 | 20 | 40 | ASKIP,FSET,NORM | BLUE |  |   | [L120](../Old_Cobol_Code/app/bms/COADM01.bms#L120) |
| COADM1A | OPTN010 | 15 | 20 | 40 | ASKIP,FSET,NORM | BLUE |  |   | [L125](../Old_Cobol_Code/app/bms/COADM01.bms#L125) |
| COADM1A | OPTN011 | 16 | 20 | 40 | ASKIP,FSET,NORM | BLUE |  |   | [L130](../Old_Cobol_Code/app/bms/COADM01.bms#L130) |
| COADM1A | OPTN012 | 17 | 20 | 40 | ASKIP,FSET,NORM | BLUE |  |   | [L135](../Old_Cobol_Code/app/bms/COADM01.bms#L135) |
| COADM1A | OPTION | 20 | 41 | 2 | FSET,IC,NORM,NUM,UNPROT |  | UNDERLINE |  | [L145](../Old_Cobol_Code/app/bms/COADM01.bms#L145) |
| COADM1A | ERRMSG | 23 | 1 | 78 | ASKIP,BRT,FSET | RED |  |  | [L154](../Old_Cobol_Code/app/bms/COADM01.bms#L154) |

## COBIL00

Source: [app/bms/COBIL00.bms](../Old_Cobol_Code/app/bms/COBIL00.bms)

| Map | Field | Row | Column | Length | ATTRB | Color | Highlight | Initial | Source |
|---|---|---:|---:|---:|---|---|---|---|---|
| COBIL0A | TRNNAME | 1 | 7 | 4 | ASKIP,FSET,NORM | BLUE |  |  | [L34](../Old_Cobol_Code/app/bms/COBIL00.bms#L34) |
| COBIL0A | TITLE01 | 1 | 21 | 40 | ASKIP,FSET,NORM | YELLOW |  |  | [L38](../Old_Cobol_Code/app/bms/COBIL00.bms#L38) |
| COBIL0A | CURDATE | 1 | 71 | 8 | ASKIP,FSET,NORM | BLUE |  | mm/dd/yy | [L47](../Old_Cobol_Code/app/bms/COBIL00.bms#L47) |
| COBIL0A | PGMNAME | 2 | 7 | 8 | ASKIP,FSET,NORM | BLUE |  |  | [L57](../Old_Cobol_Code/app/bms/COBIL00.bms#L57) |
| COBIL0A | TITLE02 | 2 | 21 | 40 | ASKIP,FSET,NORM | YELLOW |  |  | [L61](../Old_Cobol_Code/app/bms/COBIL00.bms#L61) |
| COBIL0A | CURTIME | 2 | 71 | 8 | ASKIP,FSET,NORM | BLUE |  | hh:mm:ss | [L70](../Old_Cobol_Code/app/bms/COBIL00.bms#L70) |
| COBIL0A | ACTIDIN | 6 | 21 | 11 | FSET,IC,NORM,UNPROT | GREEN | UNDERLINE |  | [L85](../Old_Cobol_Code/app/bms/COBIL00.bms#L85) |
| COBIL0A | CURBAL | 11 | 32 | 14 | ASKIP,FSET,NORM | BLUE |  |  | [L103](../Old_Cobol_Code/app/bms/COBIL00.bms#L103) |
| COBIL0A | CONFIRM | 15 | 60 | 1 | FSET,NORM,UNPROT | GREEN | UNDERLINE |  | [L115](../Old_Cobol_Code/app/bms/COBIL00.bms#L115) |
| COBIL0A | ERRMSG | 23 | 1 | 78 | ASKIP,BRT,FSET | RED |  |  | [L127](../Old_Cobol_Code/app/bms/COBIL00.bms#L127) |

## COCRDLI

Source: [app/bms/COCRDLI.bms](../Old_Cobol_Code/app/bms/COCRDLI.bms)

| Map | Field | Row | Column | Length | ATTRB | Color | Highlight | Initial | Source |
|---|---|---:|---:|---:|---|---|---|---|---|
| CCRDLIA | TRNNAME | 1 | 7 | 4 | ASKIP,FSET,NORM | BLUE |  |  | [L34](../Old_Cobol_Code/app/bms/COCRDLI.bms#L34) |
| CCRDLIA | TITLE01 | 1 | 21 | 40 | ASKIP,NORM | YELLOW |  |  | [L38](../Old_Cobol_Code/app/bms/COCRDLI.bms#L38) |
| CCRDLIA | CURDATE | 1 | 71 | 8 | ASKIP,NORM | BLUE |  | mm/dd/yy | [L47](../Old_Cobol_Code/app/bms/COCRDLI.bms#L47) |
| CCRDLIA | PGMNAME | 2 | 7 | 8 | ASKIP,NORM | BLUE |  |  | [L57](../Old_Cobol_Code/app/bms/COCRDLI.bms#L57) |
| CCRDLIA | TITLE02 | 2 | 21 | 40 | ASKIP,NORM | YELLOW |  |  | [L61](../Old_Cobol_Code/app/bms/COCRDLI.bms#L61) |
| CCRDLIA | CURTIME | 2 | 71 | 8 | ASKIP,NORM | BLUE |  | hh:mm:ss | [L70](../Old_Cobol_Code/app/bms/COCRDLI.bms#L70) |
| CCRDLIA | PAGENO | 4 | 76 | 3 |  |  |  |  | [L82](../Old_Cobol_Code/app/bms/COCRDLI.bms#L82) |
| CCRDLIA | ACCTSID | 6 | 44 | 11 | FSET,IC,NORM,UNPROT | GREEN | UNDERLINE |  | [L89](../Old_Cobol_Code/app/bms/COCRDLI.bms#L89) |
| CCRDLIA | CARDSID | 7 | 44 | 16 | FSET,NORM,UNPROT | GREEN | UNDERLINE |  | [L101](../Old_Cobol_Code/app/bms/COCRDLI.bms#L101) |
| CCRDLIA | CRDSEL1 | 11 | 12 | 1 | FSET,NORM,PROT | DEFAULT | UNDERLINE |  | [L140](../Old_Cobol_Code/app/bms/COCRDLI.bms#L140) |
| CCRDLIA | ACCTNO1 | 11 | 22 | 11 | NORM,PROT | DEFAULT | OFF |  | [L147](../Old_Cobol_Code/app/bms/COCRDLI.bms#L147) |
| CCRDLIA | CRDNUM1 | 11 | 43 | 16 | NORM,PROT | DEFAULT | OFF |  | [L152](../Old_Cobol_Code/app/bms/COCRDLI.bms#L152) |
| CCRDLIA | CRDSTS1 | 11 | 67 | 1 | NORM,PROT | DEFAULT | OFF |  | [L157](../Old_Cobol_Code/app/bms/COCRDLI.bms#L157) |
| CCRDLIA | CRDSEL2 | 12 | 12 | 1 | FSET,NORM,PROT | DEFAULT | UNDERLINE |  | [L162](../Old_Cobol_Code/app/bms/COCRDLI.bms#L162) |
| CCRDLIA | CRDSTP2 | 12 | 14 | 1 | ASKIP,DRK,FSET | DEFAULT | OFF |  | [L169](../Old_Cobol_Code/app/bms/COCRDLI.bms#L169) |
| CCRDLIA | ACCTNO2 | 12 | 22 | 11 | NORM,PROT | DEFAULT | OFF |  | [L174](../Old_Cobol_Code/app/bms/COCRDLI.bms#L174) |
| CCRDLIA | CRDNUM2 | 12 | 43 | 16 | NORM,PROT | DEFAULT | OFF |  | [L179](../Old_Cobol_Code/app/bms/COCRDLI.bms#L179) |
| CCRDLIA | CRDSTS2 | 12 | 67 | 1 | NORM,PROT | DEFAULT | OFF |  | [L184](../Old_Cobol_Code/app/bms/COCRDLI.bms#L184) |
| CCRDLIA | CRDSEL3 | 13 | 12 | 1 | FSET,NORM,PROT | DEFAULT | UNDERLINE |  | [L189](../Old_Cobol_Code/app/bms/COCRDLI.bms#L189) |
| CCRDLIA | CRDSTP3 | 13 | 14 | 1 | ASKIP,DRK,FSET | DEFAULT | OFF |  | [L196](../Old_Cobol_Code/app/bms/COCRDLI.bms#L196) |
| CCRDLIA | ACCTNO3 | 13 | 22 | 11 | NORM,PROT | DEFAULT | OFF |  | [L201](../Old_Cobol_Code/app/bms/COCRDLI.bms#L201) |
| CCRDLIA | CRDNUM3 | 13 | 43 | 16 | NORM,PROT | DEFAULT | OFF |  | [L206](../Old_Cobol_Code/app/bms/COCRDLI.bms#L206) |
| CCRDLIA | CRDSTS3 | 13 | 67 | 1 | NORM,PROT | DEFAULT | OFF |  | [L211](../Old_Cobol_Code/app/bms/COCRDLI.bms#L211) |
| CCRDLIA | CRDSEL4 | 14 | 12 | 1 | FSET,NORM,PROT | DEFAULT | UNDERLINE |  | [L216](../Old_Cobol_Code/app/bms/COCRDLI.bms#L216) |
| CCRDLIA | CRDSTP4 | 14 | 14 | 1 | ASKIP,DRK,FSET | DEFAULT | OFF |  | [L223](../Old_Cobol_Code/app/bms/COCRDLI.bms#L223) |
| CCRDLIA | ACCTNO4 | 14 | 22 | 11 | NORM,PROT | DEFAULT | OFF |  | [L228](../Old_Cobol_Code/app/bms/COCRDLI.bms#L228) |
| CCRDLIA | CRDNUM4 | 14 | 43 | 16 | NORM,PROT | DEFAULT | OFF |  | [L233](../Old_Cobol_Code/app/bms/COCRDLI.bms#L233) |
| CCRDLIA | CRDSTS4 | 14 | 67 | 1 | NORM,PROT | DEFAULT | OFF |  | [L238](../Old_Cobol_Code/app/bms/COCRDLI.bms#L238) |
| CCRDLIA | CRDSEL5 | 15 | 12 | 1 | FSET,NORM,PROT | DEFAULT | UNDERLINE |  | [L243](../Old_Cobol_Code/app/bms/COCRDLI.bms#L243) |
| CCRDLIA | CRDSTP5 | 15 | 14 | 1 | ASKIP,DRK,FSET | DEFAULT | OFF |  | [L250](../Old_Cobol_Code/app/bms/COCRDLI.bms#L250) |
| CCRDLIA | ACCTNO5 | 15 | 22 | 11 | NORM,PROT | DEFAULT | OFF |  | [L255](../Old_Cobol_Code/app/bms/COCRDLI.bms#L255) |
| CCRDLIA | CRDNUM5 | 15 | 43 | 16 | NORM,PROT | DEFAULT | OFF |  | [L260](../Old_Cobol_Code/app/bms/COCRDLI.bms#L260) |
| CCRDLIA | CRDSTS5 | 15 | 67 | 1 | NORM,PROT | DEFAULT | OFF |  | [L265](../Old_Cobol_Code/app/bms/COCRDLI.bms#L265) |
| CCRDLIA | CRDSEL6 | 16 | 12 | 1 | FSET,NORM,PROT | DEFAULT | UNDERLINE |  | [L270](../Old_Cobol_Code/app/bms/COCRDLI.bms#L270) |
| CCRDLIA | CRDSTP6 | 16 | 14 | 1 | ASKIP,DRK,FSET | DEFAULT | OFF |  | [L277](../Old_Cobol_Code/app/bms/COCRDLI.bms#L277) |
| CCRDLIA | ACCTNO6 | 16 | 22 | 11 | NORM,PROT | DEFAULT | OFF |  | [L282](../Old_Cobol_Code/app/bms/COCRDLI.bms#L282) |
| CCRDLIA | CRDNUM6 | 16 | 43 | 16 | NORM,PROT | DEFAULT | OFF |  | [L287](../Old_Cobol_Code/app/bms/COCRDLI.bms#L287) |
| CCRDLIA | CRDSTS6 | 16 | 67 | 1 | NORM,PROT | DEFAULT | OFF |  | [L292](../Old_Cobol_Code/app/bms/COCRDLI.bms#L292) |
| CCRDLIA | CRDSEL7 | 17 | 12 | 1 | FSET,NORM,PROT | DEFAULT | UNDERLINE |  | [L297](../Old_Cobol_Code/app/bms/COCRDLI.bms#L297) |
| CCRDLIA | CRDSTP7 | 17 | 14 | 1 | ASKIP,DRK,FSET | DEFAULT | OFF |  | [L304](../Old_Cobol_Code/app/bms/COCRDLI.bms#L304) |
| CCRDLIA | ACCTNO7 | 17 | 22 | 11 | NORM,PROT | DEFAULT | OFF |  | [L309](../Old_Cobol_Code/app/bms/COCRDLI.bms#L309) |
| CCRDLIA | CRDNUM7 | 17 | 43 | 16 | NORM,PROT | DEFAULT | OFF |  | [L314](../Old_Cobol_Code/app/bms/COCRDLI.bms#L314) |
| CCRDLIA | CRDSTS7 | 17 | 67 | 1 | NORM,PROT | DEFAULT | OFF |  | [L319](../Old_Cobol_Code/app/bms/COCRDLI.bms#L319) |
| CCRDLIA | INFOMSG | 20 | 19 | 45 | PROT | NEUTRAL | OFF |  | [L324](../Old_Cobol_Code/app/bms/COCRDLI.bms#L324) |
| CCRDLIA | ERRMSG | 23 | 1 | 78 | ASKIP,BRT,FSET | RED |  |  | [L331](../Old_Cobol_Code/app/bms/COCRDLI.bms#L331) |

## COCRDSL

Source: [app/bms/COCRDSL.bms](../Old_Cobol_Code/app/bms/COCRDSL.bms)

| Map | Field | Row | Column | Length | ATTRB | Color | Highlight | Initial | Source |
|---|---|---:|---:|---:|---|---|---|---|---|
| CCRDSLA | TRNNAME | 1 | 7 | 4 | ASKIP,FSET,NORM | BLUE |  |  | [L34](../Old_Cobol_Code/app/bms/COCRDSL.bms#L34) |
| CCRDSLA | TITLE01 | 1 | 21 | 40 | ASKIP,NORM | YELLOW |  |  | [L38](../Old_Cobol_Code/app/bms/COCRDSL.bms#L38) |
| CCRDSLA | CURDATE | 1 | 71 | 8 | ASKIP,NORM | BLUE |  | mm/dd/yy | [L47](../Old_Cobol_Code/app/bms/COCRDSL.bms#L47) |
| CCRDSLA | PGMNAME | 2 | 7 | 8 | ASKIP,NORM | BLUE |  |  | [L57](../Old_Cobol_Code/app/bms/COCRDSL.bms#L57) |
| CCRDSLA | TITLE02 | 2 | 21 | 40 | ASKIP,NORM | YELLOW |  |  | [L61](../Old_Cobol_Code/app/bms/COCRDSL.bms#L61) |
| CCRDSLA | CURTIME | 2 | 71 | 8 | ASKIP,NORM | BLUE |  | hh:mm:ss | [L70](../Old_Cobol_Code/app/bms/COCRDSL.bms#L70) |
| CCRDSLA | ACCTSID | 7 | 45 | 11 | FSET,IC,NORM,UNPROT | DEFAULT | UNDERLINE |  | [L84](../Old_Cobol_Code/app/bms/COCRDSL.bms#L84) |
| CCRDSLA | CARDSID | 8 | 45 | 16 | FSET,NORM,UNPROT | DEFAULT | UNDERLINE |  | [L96](../Old_Cobol_Code/app/bms/COCRDSL.bms#L96) |
| CCRDSLA | CRDNAME | 11 | 25 | 50 |  |  | UNDERLINE |  | [L107](../Old_Cobol_Code/app/bms/COCRDSL.bms#L107) |
| CCRDSLA | CRDSTCD | 13 | 25 | 1 | ASKIP |  | UNDERLINE |  | [L116](../Old_Cobol_Code/app/bms/COCRDSL.bms#L116) |
| CCRDSLA | EXPMON | 15 | 25 | 2 | ASKIP |  | UNDERLINE |  | [L126](../Old_Cobol_Code/app/bms/COCRDSL.bms#L126) |
| CCRDSLA | EXPYEAR | 15 | 30 | 4 | ASKIP |  | UNDERLINE |  | [L133](../Old_Cobol_Code/app/bms/COCRDSL.bms#L133) |
| CCRDSLA | INFOMSG | 20 | 25 | 40 | PROT | NEUTRAL | OFF |  | [L139](../Old_Cobol_Code/app/bms/COCRDSL.bms#L139) |
| CCRDSLA | ERRMSG | 23 | 1 | 80 | ASKIP,BRT,FSET | RED |  |  | [L144](../Old_Cobol_Code/app/bms/COCRDSL.bms#L144) |
| CCRDSLA | FKEYS | 24 | 1 | 75 | ASKIP,NORM | YELLOW |  | ENTER=Search Cards F3=Exit | [L148](../Old_Cobol_Code/app/bms/COCRDSL.bms#L148) |

## COCRDUP

Source: [app/bms/COCRDUP.bms](../Old_Cobol_Code/app/bms/COCRDUP.bms)

| Map | Field | Row | Column | Length | ATTRB | Color | Highlight | Initial | Source |
|---|---|---:|---:|---:|---|---|---|---|---|
| CCRDUPA | TRNNAME | 1 | 7 | 4 | ASKIP,FSET,NORM | BLUE |  |  | [L34](../Old_Cobol_Code/app/bms/COCRDUP.bms#L34) |
| CCRDUPA | TITLE01 | 1 | 21 | 40 | ASKIP,NORM | YELLOW |  |  | [L38](../Old_Cobol_Code/app/bms/COCRDUP.bms#L38) |
| CCRDUPA | CURDATE | 1 | 71 | 8 | ASKIP,NORM | BLUE |  | mm/dd/yy | [L47](../Old_Cobol_Code/app/bms/COCRDUP.bms#L47) |
| CCRDUPA | PGMNAME | 2 | 7 | 8 | ASKIP,NORM | BLUE |  |  | [L57](../Old_Cobol_Code/app/bms/COCRDUP.bms#L57) |
| CCRDUPA | TITLE02 | 2 | 21 | 40 | ASKIP,NORM | YELLOW |  |  | [L61](../Old_Cobol_Code/app/bms/COCRDUP.bms#L61) |
| CCRDUPA | CURTIME | 2 | 71 | 8 | ASKIP,NORM | BLUE |  | hh:mm:ss | [L70](../Old_Cobol_Code/app/bms/COCRDUP.bms#L70) |
| CCRDUPA | ACCTSID | 7 | 45 | 11 | FSET,IC,NORM,PROT | DEFAULT | UNDERLINE |  | [L84](../Old_Cobol_Code/app/bms/COCRDUP.bms#L84) |
| CCRDUPA | CARDSID | 8 | 45 | 16 | FSET,NORM,UNPROT | DEFAULT | UNDERLINE |  | [L96](../Old_Cobol_Code/app/bms/COCRDUP.bms#L96) |
| CCRDUPA | CRDNAME | 11 | 25 | 50 | UNPROT |  | UNDERLINE |  | [L107](../Old_Cobol_Code/app/bms/COCRDUP.bms#L107) |
| CCRDUPA | CRDSTCD | 13 | 25 | 1 | UNPROT |  | UNDERLINE |  | [L117](../Old_Cobol_Code/app/bms/COCRDUP.bms#L117) |
| CCRDUPA | EXPMON | 15 | 25 | 2 | UNPROT |  | UNDERLINE |  | [L127](../Old_Cobol_Code/app/bms/COCRDUP.bms#L127) |
| CCRDUPA | EXPYEAR | 15 | 30 | 4 | UNPROT |  | UNDERLINE |  | [L135](../Old_Cobol_Code/app/bms/COCRDUP.bms#L135) |
| CCRDUPA | EXPDAY | 15 | 36 | 2 | DRK,FSET,PROT |  | OFF |  | [L142](../Old_Cobol_Code/app/bms/COCRDUP.bms#L142) |
| CCRDUPA | INFOMSG | 20 | 25 | 40 | PROT | NEUTRAL | OFF |  | [L149](../Old_Cobol_Code/app/bms/COCRDUP.bms#L149) |
| CCRDUPA | ERRMSG | 23 | 1 | 80 | ASKIP,BRT,FSET | RED |  |  | [L154](../Old_Cobol_Code/app/bms/COCRDUP.bms#L154) |
| CCRDUPA | FKEYS | 24 | 1 | 21 | ASKIP,NORM | YELLOW |  | ENTER=Process F3=Exit | [L158](../Old_Cobol_Code/app/bms/COCRDUP.bms#L158) |
| CCRDUPA | FKEYSC | 24 | 23 | 18 | ASKIP,DRK | YELLOW |  | F5=Save F12=Cancel | [L163](../Old_Cobol_Code/app/bms/COCRDUP.bms#L163) |

## COMEN01

Source: [app/bms/COMEN01.bms](../Old_Cobol_Code/app/bms/COMEN01.bms)

| Map | Field | Row | Column | Length | ATTRB | Color | Highlight | Initial | Source |
|---|---|---:|---:|---:|---|---|---|---|---|
| COMEN1A | TRNNAME | 1 | 7 | 4 | ASKIP,FSET,NORM | BLUE |  |  | [L34](../Old_Cobol_Code/app/bms/COMEN01.bms#L34) |
| COMEN1A | TITLE01 | 1 | 21 | 40 | ASKIP,FSET,NORM | YELLOW |  |  | [L38](../Old_Cobol_Code/app/bms/COMEN01.bms#L38) |
| COMEN1A | CURDATE | 1 | 71 | 8 | ASKIP,FSET,NORM | BLUE |  | mm/dd/yy | [L47](../Old_Cobol_Code/app/bms/COMEN01.bms#L47) |
| COMEN1A | PGMNAME | 2 | 7 | 8 | ASKIP,FSET,NORM | BLUE |  |  | [L57](../Old_Cobol_Code/app/bms/COMEN01.bms#L57) |
| COMEN1A | TITLE02 | 2 | 21 | 40 | ASKIP,FSET,NORM | YELLOW |  |  | [L61](../Old_Cobol_Code/app/bms/COMEN01.bms#L61) |
| COMEN1A | CURTIME | 2 | 71 | 8 | ASKIP,FSET,NORM | BLUE |  | hh:mm:ss | [L70](../Old_Cobol_Code/app/bms/COMEN01.bms#L70) |
| COMEN1A | OPTN001 | 6 | 20 | 40 | ASKIP,FSET,NORM | BLUE |  |   | [L80](../Old_Cobol_Code/app/bms/COMEN01.bms#L80) |
| COMEN1A | OPTN002 | 7 | 20 | 40 | ASKIP,FSET,NORM | BLUE |  |   | [L85](../Old_Cobol_Code/app/bms/COMEN01.bms#L85) |
| COMEN1A | OPTN003 | 8 | 20 | 40 | ASKIP,FSET,NORM | BLUE |  |   | [L90](../Old_Cobol_Code/app/bms/COMEN01.bms#L90) |
| COMEN1A | OPTN004 | 9 | 20 | 40 | ASKIP,FSET,NORM | BLUE |  |   | [L95](../Old_Cobol_Code/app/bms/COMEN01.bms#L95) |
| COMEN1A | OPTN005 | 10 | 20 | 40 | ASKIP,FSET,NORM | BLUE |  |   | [L100](../Old_Cobol_Code/app/bms/COMEN01.bms#L100) |
| COMEN1A | OPTN006 | 11 | 20 | 40 | ASKIP,FSET,NORM | BLUE |  |   | [L105](../Old_Cobol_Code/app/bms/COMEN01.bms#L105) |
| COMEN1A | OPTN007 | 12 | 20 | 40 | ASKIP,FSET,NORM | BLUE |  |   | [L110](../Old_Cobol_Code/app/bms/COMEN01.bms#L110) |
| COMEN1A | OPTN008 | 13 | 20 | 40 | ASKIP,FSET,NORM | BLUE |  |   | [L115](../Old_Cobol_Code/app/bms/COMEN01.bms#L115) |
| COMEN1A | OPTN009 | 14 | 20 | 40 | ASKIP,FSET,NORM | BLUE |  |   | [L120](../Old_Cobol_Code/app/bms/COMEN01.bms#L120) |
| COMEN1A | OPTN010 | 15 | 20 | 40 | ASKIP,FSET,NORM | BLUE |  |   | [L125](../Old_Cobol_Code/app/bms/COMEN01.bms#L125) |
| COMEN1A | OPTN011 | 16 | 20 | 40 | ASKIP,FSET,NORM | BLUE |  |   | [L130](../Old_Cobol_Code/app/bms/COMEN01.bms#L130) |
| COMEN1A | OPTN012 | 17 | 20 | 40 | ASKIP,FSET,NORM | BLUE |  |   | [L135](../Old_Cobol_Code/app/bms/COMEN01.bms#L135) |
| COMEN1A | OPTION | 20 | 41 | 2 | FSET,IC,NORM,NUM,UNPROT |  | UNDERLINE |  | [L145](../Old_Cobol_Code/app/bms/COMEN01.bms#L145) |
| COMEN1A | ERRMSG | 23 | 1 | 78 | ASKIP,BRT,FSET | RED |  |  | [L154](../Old_Cobol_Code/app/bms/COMEN01.bms#L154) |

## CORPT00

Source: [app/bms/CORPT00.bms](../Old_Cobol_Code/app/bms/CORPT00.bms)

| Map | Field | Row | Column | Length | ATTRB | Color | Highlight | Initial | Source |
|---|---|---:|---:|---:|---|---|---|---|---|
| CORPT0A | TRNNAME | 1 | 7 | 4 | ASKIP,FSET,NORM | BLUE |  |  | [L34](../Old_Cobol_Code/app/bms/CORPT00.bms#L34) |
| CORPT0A | TITLE01 | 1 | 21 | 40 | ASKIP,FSET,NORM | YELLOW |  |  | [L38](../Old_Cobol_Code/app/bms/CORPT00.bms#L38) |
| CORPT0A | CURDATE | 1 | 71 | 8 | ASKIP,FSET,NORM | BLUE |  | mm/dd/yy | [L47](../Old_Cobol_Code/app/bms/CORPT00.bms#L47) |
| CORPT0A | PGMNAME | 2 | 7 | 8 | ASKIP,FSET,NORM | BLUE |  |  | [L57](../Old_Cobol_Code/app/bms/CORPT00.bms#L57) |
| CORPT0A | TITLE02 | 2 | 21 | 40 | ASKIP,FSET,NORM | YELLOW |  |  | [L61](../Old_Cobol_Code/app/bms/CORPT00.bms#L61) |
| CORPT0A | CURTIME | 2 | 71 | 8 | ASKIP,FSET,NORM | BLUE |  | hh:mm:ss | [L70](../Old_Cobol_Code/app/bms/CORPT00.bms#L70) |
| CORPT0A | MONTHLY | 7 | 10 | 1 | FSET,IC,NORM,UNPROT | GREEN | UNDERLINE |   | [L80](../Old_Cobol_Code/app/bms/CORPT00.bms#L80) |
| CORPT0A | YEARLY | 9 | 10 | 1 | FSET,NORM,UNPROT | GREEN | UNDERLINE |   | [L94](../Old_Cobol_Code/app/bms/CORPT00.bms#L94) |
| CORPT0A | CUSTOM | 11 | 10 | 1 | FSET,NORM,UNPROT | GREEN | UNDERLINE |   | [L108](../Old_Cobol_Code/app/bms/CORPT00.bms#L108) |
| CORPT0A | SDTMM | 13 | 29 | 2 | FSET,NORM,NUM,UNPROT | GREEN | UNDERLINE |   | [L127](../Old_Cobol_Code/app/bms/CORPT00.bms#L127) |
| CORPT0A | SDTDD | 13 | 34 | 2 | FSET,NORM,NUM,UNPROT | GREEN | UNDERLINE |   | [L138](../Old_Cobol_Code/app/bms/CORPT00.bms#L138) |
| CORPT0A | SDTYYYY | 13 | 39 | 4 | FSET,NORM,NUM,UNPROT | GREEN | UNDERLINE |   | [L149](../Old_Cobol_Code/app/bms/CORPT00.bms#L149) |
| CORPT0A | EDTMM | 14 | 29 | 2 | FSET,NORM,NUM,UNPROT | GREEN | UNDERLINE |   | [L166](../Old_Cobol_Code/app/bms/CORPT00.bms#L166) |
| CORPT0A | EDTDD | 14 | 34 | 2 | FSET,NORM,NUM,UNPROT | GREEN | UNDERLINE |   | [L177](../Old_Cobol_Code/app/bms/CORPT00.bms#L177) |
| CORPT0A | EDTYYYY | 14 | 39 | 4 | FSET,NORM,NUM,UNPROT | GREEN | UNDERLINE |   | [L188](../Old_Cobol_Code/app/bms/CORPT00.bms#L188) |
| CORPT0A | CONFIRM | 19 | 66 | 1 | FSET,NORM,UNPROT | GREEN | UNDERLINE |  | [L206](../Old_Cobol_Code/app/bms/CORPT00.bms#L206) |
| CORPT0A | ERRMSG | 23 | 1 | 78 | ASKIP,BRT,FSET | RED |  |  | [L218](../Old_Cobol_Code/app/bms/CORPT00.bms#L218) |

## COSGN00

Source: [app/bms/COSGN00.bms](../Old_Cobol_Code/app/bms/COSGN00.bms)

| Map | Field | Row | Column | Length | ATTRB | Color | Highlight | Initial | Source |
|---|---|---:|---:|---:|---|---|---|---|---|
| COSGN0A | TRNNAME | 1 | 8 | 4 | ASKIP,FSET,NORM | BLUE |  |  | [L34](../Old_Cobol_Code/app/bms/COSGN00.bms#L34) |
| COSGN0A | TITLE01 | 1 | 21 | 40 | ASKIP,FSET,NORM | YELLOW |  |  | [L38](../Old_Cobol_Code/app/bms/COSGN00.bms#L38) |
| COSGN0A | CURDATE | 1 | 71 | 8 | ASKIP,FSET,NORM | BLUE |  | mm/dd/yy | [L47](../Old_Cobol_Code/app/bms/COSGN00.bms#L47) |
| COSGN0A | PGMNAME | 2 | 8 | 8 | FSET,NORM,PROT | BLUE |  |  | [L57](../Old_Cobol_Code/app/bms/COSGN00.bms#L57) |
| COSGN0A | TITLE02 | 2 | 21 | 40 | ASKIP,FSET,NORM | YELLOW |  |  | [L61](../Old_Cobol_Code/app/bms/COSGN00.bms#L61) |
| COSGN0A | CURTIME | 2 | 71 | 9 | FSET,NORM,PROT | BLUE |  | Ahh:mm:ss | [L70](../Old_Cobol_Code/app/bms/COSGN00.bms#L70) |
| COSGN0A | APPLID | 3 | 8 | 8 | FSET,NORM,PROT | BLUE |  |  | [L80](../Old_Cobol_Code/app/bms/COSGN00.bms#L80) |
| COSGN0A | SYSID | 3 | 71 | 8 | FSET,NORM,PROT | BLUE |  |   | [L89](../Old_Cobol_Code/app/bms/COSGN00.bms#L89) |
| COSGN0A | USERID | 19 | 43 | 8 | FSET,IC,NORM,UNPROT | GREEN | OFF |  | [L156](../Old_Cobol_Code/app/bms/COSGN00.bms#L156) |
| COSGN0A | PASSWD | 20 | 43 | 8 | DRK,FSET,UNPROT | GREEN | OFF | ________ | [L175](../Old_Cobol_Code/app/bms/COSGN00.bms#L175) |
| COSGN0A | ERRMSG | 23 | 1 | 78 | ASKIP,BRT,FSET | RED |  |  | [L197](../Old_Cobol_Code/app/bms/COSGN00.bms#L197) |

## COTRN00

Source: [app/bms/COTRN00.bms](../Old_Cobol_Code/app/bms/COTRN00.bms)

| Map | Field | Row | Column | Length | ATTRB | Color | Highlight | Initial | Source |
|---|---|---:|---:|---:|---|---|---|---|---|
| COTRN0A | TRNNAME | 1 | 7 | 4 | ASKIP,FSET,NORM | BLUE |  |  | [L34](../Old_Cobol_Code/app/bms/COTRN00.bms#L34) |
| COTRN0A | TITLE01 | 1 | 21 | 40 | ASKIP,FSET,NORM | YELLOW |  |  | [L38](../Old_Cobol_Code/app/bms/COTRN00.bms#L38) |
| COTRN0A | CURDATE | 1 | 71 | 8 | ASKIP,FSET,NORM | BLUE |  | mm/dd/yy | [L47](../Old_Cobol_Code/app/bms/COTRN00.bms#L47) |
| COTRN0A | PGMNAME | 2 | 7 | 8 | ASKIP,FSET,NORM | BLUE |  |  | [L57](../Old_Cobol_Code/app/bms/COTRN00.bms#L57) |
| COTRN0A | TITLE02 | 2 | 21 | 40 | ASKIP,FSET,NORM | YELLOW |  |  | [L61](../Old_Cobol_Code/app/bms/COTRN00.bms#L61) |
| COTRN0A | CURTIME | 2 | 71 | 8 | ASKIP,FSET,NORM | BLUE |  | hh:mm:ss | [L70](../Old_Cobol_Code/app/bms/COTRN00.bms#L70) |
| COTRN0A | PAGENUM | 4 | 71 | 8 | ASKIP,FSET,NORM | BLUE |  |   | [L85](../Old_Cobol_Code/app/bms/COTRN00.bms#L85) |
| COTRN0A | TRNIDIN | 6 | 21 | 16 | FSET,NORM,UNPROT | GREEN | UNDERLINE |  | [L95](../Old_Cobol_Code/app/bms/COTRN00.bms#L95) |
| COTRN0A | SEL0001 | 10 | 3 | 1 | FSET,NORM,UNPROT | GREEN | UNDERLINE |   | [L153](../Old_Cobol_Code/app/bms/COTRN00.bms#L153) |
| COTRN0A | TRNID01 | 10 | 8 | 16 | ASKIP,FSET,NORM | BLUE |  |   | [L162](../Old_Cobol_Code/app/bms/COTRN00.bms#L162) |
| COTRN0A | TDATE01 | 10 | 27 | 8 | ASKIP,FSET,NORM | BLUE |  |   | [L167](../Old_Cobol_Code/app/bms/COTRN00.bms#L167) |
| COTRN0A | TDESC01 | 10 | 38 | 26 | ASKIP,FSET,NORM | BLUE |  |   | [L172](../Old_Cobol_Code/app/bms/COTRN00.bms#L172) |
| COTRN0A | TAMT001 | 10 | 67 | 12 | ASKIP,FSET,NORM | BLUE |  |   | [L177](../Old_Cobol_Code/app/bms/COTRN00.bms#L177) |
| COTRN0A | SEL0002 | 11 | 3 | 1 | FSET,NORM,UNPROT | GREEN | UNDERLINE |   | [L182](../Old_Cobol_Code/app/bms/COTRN00.bms#L182) |
| COTRN0A | TRNID02 | 11 | 8 | 16 | ASKIP,FSET,NORM | BLUE |  |   | [L191](../Old_Cobol_Code/app/bms/COTRN00.bms#L191) |
| COTRN0A | TDATE02 | 11 | 27 | 8 | ASKIP,FSET,NORM | BLUE |  |   | [L196](../Old_Cobol_Code/app/bms/COTRN00.bms#L196) |
| COTRN0A | TDESC02 | 11 | 38 | 26 | ASKIP,FSET,NORM | BLUE |  |   | [L201](../Old_Cobol_Code/app/bms/COTRN00.bms#L201) |
| COTRN0A | TAMT002 | 11 | 67 | 12 | ASKIP,FSET,NORM | BLUE |  |   | [L206](../Old_Cobol_Code/app/bms/COTRN00.bms#L206) |
| COTRN0A | SEL0003 | 12 | 3 | 1 | FSET,NORM,UNPROT | GREEN | UNDERLINE |   | [L211](../Old_Cobol_Code/app/bms/COTRN00.bms#L211) |
| COTRN0A | TRNID03 | 12 | 8 | 16 | ASKIP,FSET,NORM | BLUE |  |   | [L220](../Old_Cobol_Code/app/bms/COTRN00.bms#L220) |
| COTRN0A | TDATE03 | 12 | 27 | 8 | ASKIP,FSET,NORM | BLUE |  |   | [L225](../Old_Cobol_Code/app/bms/COTRN00.bms#L225) |
| COTRN0A | TDESC03 | 12 | 38 | 26 | ASKIP,FSET,NORM | BLUE |  |   | [L230](../Old_Cobol_Code/app/bms/COTRN00.bms#L230) |
| COTRN0A | TAMT003 | 12 | 67 | 12 | ASKIP,FSET,NORM | BLUE |  |   | [L235](../Old_Cobol_Code/app/bms/COTRN00.bms#L235) |
| COTRN0A | SEL0004 | 13 | 3 | 1 | FSET,NORM,UNPROT | GREEN | UNDERLINE |   | [L240](../Old_Cobol_Code/app/bms/COTRN00.bms#L240) |
| COTRN0A | TRNID04 | 13 | 8 | 16 | ASKIP,FSET,NORM | BLUE |  |   | [L249](../Old_Cobol_Code/app/bms/COTRN00.bms#L249) |
| COTRN0A | TDATE04 | 13 | 27 | 8 | ASKIP,FSET,NORM | BLUE |  |   | [L254](../Old_Cobol_Code/app/bms/COTRN00.bms#L254) |
| COTRN0A | TDESC04 | 13 | 38 | 26 | ASKIP,FSET,NORM | BLUE |  |   | [L259](../Old_Cobol_Code/app/bms/COTRN00.bms#L259) |
| COTRN0A | TAMT004 | 13 | 67 | 12 | ASKIP,FSET,NORM | BLUE |  |   | [L264](../Old_Cobol_Code/app/bms/COTRN00.bms#L264) |
| COTRN0A | SEL0005 | 14 | 3 | 1 | FSET,NORM,UNPROT | GREEN | UNDERLINE |   | [L269](../Old_Cobol_Code/app/bms/COTRN00.bms#L269) |
| COTRN0A | TRNID05 | 14 | 8 | 16 | ASKIP,FSET,NORM | BLUE |  |   | [L278](../Old_Cobol_Code/app/bms/COTRN00.bms#L278) |
| COTRN0A | TDATE05 | 14 | 27 | 8 | ASKIP,FSET,NORM | BLUE |  |   | [L283](../Old_Cobol_Code/app/bms/COTRN00.bms#L283) |
| COTRN0A | TDESC05 | 14 | 38 | 26 | ASKIP,FSET,NORM | BLUE |  |   | [L288](../Old_Cobol_Code/app/bms/COTRN00.bms#L288) |
| COTRN0A | TAMT005 | 14 | 67 | 12 | ASKIP,FSET,NORM | BLUE |  |   | [L293](../Old_Cobol_Code/app/bms/COTRN00.bms#L293) |
| COTRN0A | SEL0006 | 15 | 3 | 1 | FSET,NORM,UNPROT | GREEN | UNDERLINE |   | [L298](../Old_Cobol_Code/app/bms/COTRN00.bms#L298) |
| COTRN0A | TRNID06 | 15 | 8 | 16 | ASKIP,FSET,NORM | BLUE |  |   | [L307](../Old_Cobol_Code/app/bms/COTRN00.bms#L307) |
| COTRN0A | TDATE06 | 15 | 27 | 8 | ASKIP,FSET,NORM | BLUE |  |   | [L312](../Old_Cobol_Code/app/bms/COTRN00.bms#L312) |
| COTRN0A | TDESC06 | 15 | 38 | 26 | ASKIP,FSET,NORM | BLUE |  |   | [L317](../Old_Cobol_Code/app/bms/COTRN00.bms#L317) |
| COTRN0A | TAMT006 | 15 | 67 | 12 | ASKIP,FSET,NORM | BLUE |  |   | [L322](../Old_Cobol_Code/app/bms/COTRN00.bms#L322) |
| COTRN0A | SEL0007 | 16 | 3 | 1 | FSET,NORM,UNPROT | GREEN | UNDERLINE |   | [L327](../Old_Cobol_Code/app/bms/COTRN00.bms#L327) |
| COTRN0A | TRNID07 | 16 | 8 | 16 | ASKIP,FSET,NORM | BLUE |  |   | [L336](../Old_Cobol_Code/app/bms/COTRN00.bms#L336) |
| COTRN0A | TDATE07 | 16 | 27 | 8 | ASKIP,FSET,NORM | BLUE |  |   | [L341](../Old_Cobol_Code/app/bms/COTRN00.bms#L341) |
| COTRN0A | TDESC07 | 16 | 38 | 26 | ASKIP,FSET,NORM | BLUE |  |   | [L346](../Old_Cobol_Code/app/bms/COTRN00.bms#L346) |
| COTRN0A | TAMT007 | 16 | 67 | 12 | ASKIP,FSET,NORM | BLUE |  |   | [L351](../Old_Cobol_Code/app/bms/COTRN00.bms#L351) |
| COTRN0A | SEL0008 | 17 | 3 | 1 | FSET,NORM,UNPROT | GREEN | UNDERLINE |   | [L356](../Old_Cobol_Code/app/bms/COTRN00.bms#L356) |
| COTRN0A | TRNID08 | 17 | 8 | 16 | ASKIP,FSET,NORM | BLUE |  |   | [L365](../Old_Cobol_Code/app/bms/COTRN00.bms#L365) |
| COTRN0A | TDATE08 | 17 | 27 | 8 | ASKIP,FSET,NORM | BLUE |  |   | [L370](../Old_Cobol_Code/app/bms/COTRN00.bms#L370) |
| COTRN0A | TDESC08 | 17 | 38 | 26 | ASKIP,FSET,NORM | BLUE |  |   | [L375](../Old_Cobol_Code/app/bms/COTRN00.bms#L375) |
| COTRN0A | TAMT008 | 17 | 67 | 12 | ASKIP,FSET,NORM | BLUE |  |   | [L380](../Old_Cobol_Code/app/bms/COTRN00.bms#L380) |
| COTRN0A | SEL0009 | 18 | 3 | 1 | FSET,NORM,UNPROT | GREEN | UNDERLINE |   | [L385](../Old_Cobol_Code/app/bms/COTRN00.bms#L385) |
| COTRN0A | TRNID09 | 18 | 8 | 16 | ASKIP,FSET,NORM | BLUE |  |   | [L394](../Old_Cobol_Code/app/bms/COTRN00.bms#L394) |
| COTRN0A | TDATE09 | 18 | 27 | 8 | ASKIP,FSET,NORM | BLUE |  |   | [L399](../Old_Cobol_Code/app/bms/COTRN00.bms#L399) |
| COTRN0A | TDESC09 | 18 | 38 | 26 | ASKIP,FSET,NORM | BLUE |  |   | [L404](../Old_Cobol_Code/app/bms/COTRN00.bms#L404) |
| COTRN0A | TAMT009 | 18 | 67 | 12 | ASKIP,FSET,NORM | BLUE |  |   | [L409](../Old_Cobol_Code/app/bms/COTRN00.bms#L409) |
| COTRN0A | SEL0010 | 19 | 3 | 1 | FSET,NORM,UNPROT | GREEN | UNDERLINE |   | [L414](../Old_Cobol_Code/app/bms/COTRN00.bms#L414) |
| COTRN0A | TRNID10 | 19 | 8 | 16 | ASKIP,FSET,NORM | BLUE |  |   | [L423](../Old_Cobol_Code/app/bms/COTRN00.bms#L423) |
| COTRN0A | TDATE10 | 19 | 27 | 8 | ASKIP,FSET,NORM | BLUE |  |   | [L428](../Old_Cobol_Code/app/bms/COTRN00.bms#L428) |
| COTRN0A | TDESC10 | 19 | 38 | 26 | ASKIP,FSET,NORM | BLUE |  |   | [L433](../Old_Cobol_Code/app/bms/COTRN00.bms#L433) |
| COTRN0A | TAMT010 | 19 | 67 | 12 | ASKIP,FSET,NORM | BLUE |  |   | [L438](../Old_Cobol_Code/app/bms/COTRN00.bms#L438) |
| COTRN0A | ERRMSG | 23 | 1 | 78 | ASKIP,BRT,FSET | RED |  |  | [L450](../Old_Cobol_Code/app/bms/COTRN00.bms#L450) |

## COTRN01

Source: [app/bms/COTRN01.bms](../Old_Cobol_Code/app/bms/COTRN01.bms)

| Map | Field | Row | Column | Length | ATTRB | Color | Highlight | Initial | Source |
|---|---|---:|---:|---:|---|---|---|---|---|
| COTRN1A | TRNNAME | 1 | 7 | 4 | ASKIP,FSET,NORM | BLUE |  |  | [L34](../Old_Cobol_Code/app/bms/COTRN01.bms#L34) |
| COTRN1A | TITLE01 | 1 | 21 | 40 | ASKIP,FSET,NORM | YELLOW |  |  | [L38](../Old_Cobol_Code/app/bms/COTRN01.bms#L38) |
| COTRN1A | CURDATE | 1 | 71 | 8 | ASKIP,FSET,NORM | BLUE |  | mm/dd/yy | [L47](../Old_Cobol_Code/app/bms/COTRN01.bms#L47) |
| COTRN1A | PGMNAME | 2 | 7 | 8 | ASKIP,FSET,NORM | BLUE |  |  | [L57](../Old_Cobol_Code/app/bms/COTRN01.bms#L57) |
| COTRN1A | TITLE02 | 2 | 21 | 40 | ASKIP,FSET,NORM | YELLOW |  |  | [L61](../Old_Cobol_Code/app/bms/COTRN01.bms#L61) |
| COTRN1A | CURTIME | 2 | 71 | 8 | ASKIP,FSET,NORM | BLUE |  | hh:mm:ss | [L70](../Old_Cobol_Code/app/bms/COTRN01.bms#L70) |
| COTRN1A | TRNIDIN | 6 | 21 | 16 | FSET,IC,NORM,UNPROT | GREEN | UNDERLINE |   | [L85](../Old_Cobol_Code/app/bms/COTRN01.bms#L85) |
| COTRN1A | TRNID | 10 | 22 | 16 | ASKIP,NORM | BLUE |  |   | [L105](../Old_Cobol_Code/app/bms/COTRN01.bms#L105) |
| COTRN1A | CARDNUM | 10 | 58 | 16 | ASKIP,NORM | BLUE |  |   | [L118](../Old_Cobol_Code/app/bms/COTRN01.bms#L118) |
| COTRN1A | TTYPCD | 12 | 15 | 2 | ASKIP,NORM | BLUE |  |   | [L132](../Old_Cobol_Code/app/bms/COTRN01.bms#L132) |
| COTRN1A | TCATCD | 12 | 36 | 4 | ASKIP,NORM | BLUE |  |   | [L144](../Old_Cobol_Code/app/bms/COTRN01.bms#L144) |
| COTRN1A | TRNSRC | 12 | 54 | 10 | ASKIP,NORM | BLUE |  |   | [L156](../Old_Cobol_Code/app/bms/COTRN01.bms#L156) |
| COTRN1A | TDESC | 14 | 19 | 60 | ASKIP,NORM | BLUE |  |   | [L168](../Old_Cobol_Code/app/bms/COTRN01.bms#L168) |
| COTRN1A | TRNAMT | 16 | 14 | 12 | ASKIP,NORM | BLUE |  |   | [L180](../Old_Cobol_Code/app/bms/COTRN01.bms#L180) |
| COTRN1A | TORIGDT | 16 | 42 | 10 | ASKIP,NORM | BLUE |  |   | [L192](../Old_Cobol_Code/app/bms/COTRN01.bms#L192) |
| COTRN1A | TPROCDT | 16 | 68 | 10 | ASKIP,NORM | BLUE |  |   | [L204](../Old_Cobol_Code/app/bms/COTRN01.bms#L204) |
| COTRN1A | MID | 18 | 19 | 9 | ASKIP,NORM | BLUE |  |   | [L216](../Old_Cobol_Code/app/bms/COTRN01.bms#L216) |
| COTRN1A | MNAME | 18 | 48 | 30 | ASKIP,NORM | BLUE |  |   | [L228](../Old_Cobol_Code/app/bms/COTRN01.bms#L228) |
| COTRN1A | MCITY | 20 | 21 | 25 | ASKIP,NORM | BLUE |  |   | [L240](../Old_Cobol_Code/app/bms/COTRN01.bms#L240) |
| COTRN1A | MZIP | 20 | 67 | 10 | ASKIP,NORM | BLUE |  |   | [L252](../Old_Cobol_Code/app/bms/COTRN01.bms#L252) |
| COTRN1A | ERRMSG | 23 | 1 | 78 | ASKIP,BRT,FSET | RED |  |  | [L259](../Old_Cobol_Code/app/bms/COTRN01.bms#L259) |

## COTRN02

Source: [app/bms/COTRN02.bms](../Old_Cobol_Code/app/bms/COTRN02.bms)

| Map | Field | Row | Column | Length | ATTRB | Color | Highlight | Initial | Source |
|---|---|---:|---:|---:|---|---|---|---|---|
| COTRN2A | TRNNAME | 1 | 7 | 4 | ASKIP,FSET,NORM | BLUE |  |  | [L34](../Old_Cobol_Code/app/bms/COTRN02.bms#L34) |
| COTRN2A | TITLE01 | 1 | 21 | 40 | ASKIP,FSET,NORM | YELLOW |  |  | [L38](../Old_Cobol_Code/app/bms/COTRN02.bms#L38) |
| COTRN2A | CURDATE | 1 | 71 | 8 | ASKIP,FSET,NORM | BLUE |  | mm/dd/yy | [L47](../Old_Cobol_Code/app/bms/COTRN02.bms#L47) |
| COTRN2A | PGMNAME | 2 | 7 | 8 | ASKIP,FSET,NORM | BLUE |  |  | [L57](../Old_Cobol_Code/app/bms/COTRN02.bms#L57) |
| COTRN2A | TITLE02 | 2 | 21 | 40 | ASKIP,FSET,NORM | YELLOW |  |  | [L61](../Old_Cobol_Code/app/bms/COTRN02.bms#L61) |
| COTRN2A | CURTIME | 2 | 71 | 8 | ASKIP,FSET,NORM | BLUE |  | hh:mm:ss | [L70](../Old_Cobol_Code/app/bms/COTRN02.bms#L70) |
| COTRN2A | ACTIDIN | 6 | 21 | 11 | FSET,IC,NORM,UNPROT | GREEN | UNDERLINE |   | [L85](../Old_Cobol_Code/app/bms/COTRN02.bms#L85) |
| COTRN2A | CARDNIN | 6 | 55 | 16 | FSET,NORM,UNPROT | GREEN | UNDERLINE |  | [L104](../Old_Cobol_Code/app/bms/COTRN02.bms#L104) |
| COTRN2A | TTYPCD | 10 | 15 | 2 | FSET,NORM,UNPROT | GREEN | UNDERLINE |   | [L122](../Old_Cobol_Code/app/bms/COTRN02.bms#L122) |
| COTRN2A | TCATCD | 10 | 36 | 4 | FSET,NORM,UNPROT | GREEN | UNDERLINE |   | [L135](../Old_Cobol_Code/app/bms/COTRN02.bms#L135) |
| COTRN2A | TRNSRC | 10 | 54 | 10 | FSET,NORM,UNPROT | GREEN | UNDERLINE |   | [L148](../Old_Cobol_Code/app/bms/COTRN02.bms#L148) |
| COTRN2A | TDESC | 12 | 19 | 60 | FSET,NORM,UNPROT | GREEN | UNDERLINE |   | [L161](../Old_Cobol_Code/app/bms/COTRN02.bms#L161) |
| COTRN2A | TRNAMT | 14 | 14 | 12 | FSET,NORM,UNPROT | GREEN | UNDERLINE |   | [L174](../Old_Cobol_Code/app/bms/COTRN02.bms#L174) |
| COTRN2A | TORIGDT | 14 | 42 | 10 | FSET,NORM,UNPROT | GREEN | UNDERLINE |   | [L187](../Old_Cobol_Code/app/bms/COTRN02.bms#L187) |
| COTRN2A | TPROCDT | 14 | 68 | 10 | FSET,NORM,UNPROT | GREEN | UNDERLINE |   | [L200](../Old_Cobol_Code/app/bms/COTRN02.bms#L200) |
| COTRN2A | MID | 16 | 19 | 9 | FSET,NORM,UNPROT | GREEN | UNDERLINE |   | [L228](../Old_Cobol_Code/app/bms/COTRN02.bms#L228) |
| COTRN2A | MNAME | 16 | 48 | 30 | FSET,NORM,UNPROT | GREEN | UNDERLINE |   | [L241](../Old_Cobol_Code/app/bms/COTRN02.bms#L241) |
| COTRN2A | MCITY | 18 | 21 | 25 | FSET,NORM,UNPROT | GREEN | UNDERLINE |   | [L254](../Old_Cobol_Code/app/bms/COTRN02.bms#L254) |
| COTRN2A | MZIP | 18 | 67 | 10 | FSET,NORM,UNPROT | GREEN | UNDERLINE |   | [L267](../Old_Cobol_Code/app/bms/COTRN02.bms#L267) |
| COTRN2A | CONFIRM | 21 | 63 | 1 | FSET,NORM,UNPROT | GREEN | UNDERLINE |  | [L281](../Old_Cobol_Code/app/bms/COTRN02.bms#L281) |
| COTRN2A | ERRMSG | 23 | 1 | 78 | ASKIP,BRT,FSET | RED |  |  | [L293](../Old_Cobol_Code/app/bms/COTRN02.bms#L293) |

## COUSR00

Source: [app/bms/COUSR00.bms](../Old_Cobol_Code/app/bms/COUSR00.bms)

| Map | Field | Row | Column | Length | ATTRB | Color | Highlight | Initial | Source |
|---|---|---:|---:|---:|---|---|---|---|---|
| COUSR0A | TRNNAME | 1 | 7 | 4 | ASKIP,FSET,NORM | BLUE |  |  | [L34](../Old_Cobol_Code/app/bms/COUSR00.bms#L34) |
| COUSR0A | TITLE01 | 1 | 21 | 40 | ASKIP,FSET,NORM | YELLOW |  |  | [L38](../Old_Cobol_Code/app/bms/COUSR00.bms#L38) |
| COUSR0A | CURDATE | 1 | 71 | 8 | ASKIP,FSET,NORM | BLUE |  | mm/dd/yy | [L47](../Old_Cobol_Code/app/bms/COUSR00.bms#L47) |
| COUSR0A | PGMNAME | 2 | 7 | 8 | ASKIP,FSET,NORM | BLUE |  |  | [L57](../Old_Cobol_Code/app/bms/COUSR00.bms#L57) |
| COUSR0A | TITLE02 | 2 | 21 | 40 | ASKIP,FSET,NORM | YELLOW |  |  | [L61](../Old_Cobol_Code/app/bms/COUSR00.bms#L61) |
| COUSR0A | CURTIME | 2 | 71 | 8 | ASKIP,FSET,NORM | BLUE |  | hh:mm:ss | [L70](../Old_Cobol_Code/app/bms/COUSR00.bms#L70) |
| COUSR0A | PAGENUM | 4 | 71 | 8 | ASKIP,FSET,NORM | BLUE |  |   | [L85](../Old_Cobol_Code/app/bms/COUSR00.bms#L85) |
| COUSR0A | USRIDIN | 6 | 21 | 8 | FSET,NORM,UNPROT | GREEN | UNDERLINE |  | [L95](../Old_Cobol_Code/app/bms/COUSR00.bms#L95) |
| COUSR0A | SEL0001 | 10 | 6 | 1 | FSET,NORM,UNPROT | GREEN | UNDERLINE |   | [L153](../Old_Cobol_Code/app/bms/COUSR00.bms#L153) |
| COUSR0A | USRID01 | 10 | 12 | 8 | ASKIP,FSET,NORM | BLUE |  |   | [L162](../Old_Cobol_Code/app/bms/COUSR00.bms#L162) |
| COUSR0A | FNAME01 | 10 | 24 | 20 | ASKIP,FSET,NORM | BLUE |  |   | [L167](../Old_Cobol_Code/app/bms/COUSR00.bms#L167) |
| COUSR0A | LNAME01 | 10 | 48 | 20 | ASKIP,FSET,NORM | BLUE |  |   | [L172](../Old_Cobol_Code/app/bms/COUSR00.bms#L172) |
| COUSR0A | UTYPE01 | 10 | 73 | 1 | ASKIP,FSET,NORM | BLUE |  |   | [L177](../Old_Cobol_Code/app/bms/COUSR00.bms#L177) |
| COUSR0A | SEL0002 | 11 | 6 | 1 | FSET,NORM,UNPROT | GREEN | UNDERLINE |   | [L182](../Old_Cobol_Code/app/bms/COUSR00.bms#L182) |
| COUSR0A | USRID02 | 11 | 12 | 8 | ASKIP,FSET,NORM | BLUE |  |   | [L191](../Old_Cobol_Code/app/bms/COUSR00.bms#L191) |
| COUSR0A | FNAME02 | 11 | 24 | 20 | ASKIP,FSET,NORM | BLUE |  |   | [L196](../Old_Cobol_Code/app/bms/COUSR00.bms#L196) |
| COUSR0A | LNAME02 | 11 | 48 | 20 | ASKIP,FSET,NORM | BLUE |  |   | [L201](../Old_Cobol_Code/app/bms/COUSR00.bms#L201) |
| COUSR0A | UTYPE02 | 11 | 73 | 1 | ASKIP,FSET,NORM | BLUE |  |   | [L206](../Old_Cobol_Code/app/bms/COUSR00.bms#L206) |
| COUSR0A | SEL0003 | 12 | 6 | 1 | FSET,NORM,UNPROT | GREEN | UNDERLINE |   | [L211](../Old_Cobol_Code/app/bms/COUSR00.bms#L211) |
| COUSR0A | USRID03 | 12 | 12 | 8 | ASKIP,FSET,NORM | BLUE |  |   | [L220](../Old_Cobol_Code/app/bms/COUSR00.bms#L220) |
| COUSR0A | FNAME03 | 12 | 24 | 20 | ASKIP,FSET,NORM | BLUE |  |   | [L225](../Old_Cobol_Code/app/bms/COUSR00.bms#L225) |
| COUSR0A | LNAME03 | 12 | 48 | 20 | ASKIP,FSET,NORM | BLUE |  |   | [L230](../Old_Cobol_Code/app/bms/COUSR00.bms#L230) |
| COUSR0A | UTYPE03 | 12 | 73 | 1 | ASKIP,FSET,NORM | BLUE |  |   | [L235](../Old_Cobol_Code/app/bms/COUSR00.bms#L235) |
| COUSR0A | SEL0004 | 13 | 6 | 1 | FSET,NORM,UNPROT | GREEN | UNDERLINE |   | [L240](../Old_Cobol_Code/app/bms/COUSR00.bms#L240) |
| COUSR0A | USRID04 | 13 | 12 | 8 | ASKIP,FSET,NORM | BLUE |  |   | [L249](../Old_Cobol_Code/app/bms/COUSR00.bms#L249) |
| COUSR0A | FNAME04 | 13 | 24 | 20 | ASKIP,FSET,NORM | BLUE |  |   | [L254](../Old_Cobol_Code/app/bms/COUSR00.bms#L254) |
| COUSR0A | LNAME04 | 13 | 48 | 20 | ASKIP,FSET,NORM | BLUE |  |   | [L259](../Old_Cobol_Code/app/bms/COUSR00.bms#L259) |
| COUSR0A | UTYPE04 | 13 | 73 | 1 | ASKIP,FSET,NORM | BLUE |  |   | [L264](../Old_Cobol_Code/app/bms/COUSR00.bms#L264) |
| COUSR0A | SEL0005 | 14 | 6 | 1 | FSET,NORM,UNPROT | GREEN | UNDERLINE |   | [L269](../Old_Cobol_Code/app/bms/COUSR00.bms#L269) |
| COUSR0A | USRID05 | 14 | 12 | 8 | ASKIP,FSET,NORM | BLUE |  |   | [L278](../Old_Cobol_Code/app/bms/COUSR00.bms#L278) |
| COUSR0A | FNAME05 | 14 | 24 | 20 | ASKIP,FSET,NORM | BLUE |  |   | [L283](../Old_Cobol_Code/app/bms/COUSR00.bms#L283) |
| COUSR0A | LNAME05 | 14 | 48 | 20 | ASKIP,FSET,NORM | BLUE |  |   | [L288](../Old_Cobol_Code/app/bms/COUSR00.bms#L288) |
| COUSR0A | UTYPE05 | 14 | 73 | 1 | ASKIP,FSET,NORM | BLUE |  |   | [L293](../Old_Cobol_Code/app/bms/COUSR00.bms#L293) |
| COUSR0A | SEL0006 | 15 | 6 | 1 | FSET,NORM,UNPROT | GREEN | UNDERLINE |   | [L298](../Old_Cobol_Code/app/bms/COUSR00.bms#L298) |
| COUSR0A | USRID06 | 15 | 12 | 8 | ASKIP,FSET,NORM | BLUE |  |   | [L307](../Old_Cobol_Code/app/bms/COUSR00.bms#L307) |
| COUSR0A | FNAME06 | 15 | 24 | 20 | ASKIP,FSET,NORM | BLUE |  |   | [L312](../Old_Cobol_Code/app/bms/COUSR00.bms#L312) |
| COUSR0A | LNAME06 | 15 | 48 | 20 | ASKIP,FSET,NORM | BLUE |  |   | [L317](../Old_Cobol_Code/app/bms/COUSR00.bms#L317) |
| COUSR0A | UTYPE06 | 15 | 73 | 1 | ASKIP,FSET,NORM | BLUE |  |   | [L322](../Old_Cobol_Code/app/bms/COUSR00.bms#L322) |
| COUSR0A | SEL0007 | 16 | 6 | 1 | FSET,NORM,UNPROT | GREEN | UNDERLINE |   | [L327](../Old_Cobol_Code/app/bms/COUSR00.bms#L327) |
| COUSR0A | USRID07 | 16 | 12 | 8 | ASKIP,FSET,NORM | BLUE |  |   | [L336](../Old_Cobol_Code/app/bms/COUSR00.bms#L336) |
| COUSR0A | FNAME07 | 16 | 24 | 20 | ASKIP,FSET,NORM | BLUE |  |   | [L341](../Old_Cobol_Code/app/bms/COUSR00.bms#L341) |
| COUSR0A | LNAME07 | 16 | 48 | 20 | ASKIP,FSET,NORM | BLUE |  |   | [L346](../Old_Cobol_Code/app/bms/COUSR00.bms#L346) |
| COUSR0A | UTYPE07 | 16 | 73 | 1 | ASKIP,FSET,NORM | BLUE |  |   | [L351](../Old_Cobol_Code/app/bms/COUSR00.bms#L351) |
| COUSR0A | SEL0008 | 17 | 6 | 1 | FSET,NORM,UNPROT | GREEN | UNDERLINE |   | [L356](../Old_Cobol_Code/app/bms/COUSR00.bms#L356) |
| COUSR0A | USRID08 | 17 | 12 | 8 | ASKIP,FSET,NORM | BLUE |  |   | [L365](../Old_Cobol_Code/app/bms/COUSR00.bms#L365) |
| COUSR0A | FNAME08 | 17 | 24 | 20 | ASKIP,FSET,NORM | BLUE |  |   | [L370](../Old_Cobol_Code/app/bms/COUSR00.bms#L370) |
| COUSR0A | LNAME08 | 17 | 48 | 20 | ASKIP,FSET,NORM | BLUE |  |   | [L375](../Old_Cobol_Code/app/bms/COUSR00.bms#L375) |
| COUSR0A | UTYPE08 | 17 | 73 | 1 | ASKIP,FSET,NORM | BLUE |  |   | [L380](../Old_Cobol_Code/app/bms/COUSR00.bms#L380) |
| COUSR0A | SEL0009 | 18 | 6 | 1 | FSET,NORM,UNPROT | GREEN | UNDERLINE |   | [L385](../Old_Cobol_Code/app/bms/COUSR00.bms#L385) |
| COUSR0A | USRID09 | 18 | 12 | 8 | ASKIP,FSET,NORM | BLUE |  |   | [L394](../Old_Cobol_Code/app/bms/COUSR00.bms#L394) |
| COUSR0A | FNAME09 | 18 | 24 | 20 | ASKIP,FSET,NORM | BLUE |  |   | [L399](../Old_Cobol_Code/app/bms/COUSR00.bms#L399) |
| COUSR0A | LNAME09 | 18 | 48 | 20 | ASKIP,FSET,NORM | BLUE |  |   | [L404](../Old_Cobol_Code/app/bms/COUSR00.bms#L404) |
| COUSR0A | UTYPE09 | 18 | 73 | 1 | ASKIP,FSET,NORM | BLUE |  |   | [L409](../Old_Cobol_Code/app/bms/COUSR00.bms#L409) |
| COUSR0A | SEL0010 | 19 | 6 | 1 | FSET,NORM,UNPROT | GREEN | UNDERLINE |   | [L414](../Old_Cobol_Code/app/bms/COUSR00.bms#L414) |
| COUSR0A | USRID10 | 19 | 12 | 8 | ASKIP,FSET,NORM | BLUE |  |   | [L423](../Old_Cobol_Code/app/bms/COUSR00.bms#L423) |
| COUSR0A | FNAME10 | 19 | 24 | 20 | ASKIP,FSET,NORM | BLUE |  |   | [L428](../Old_Cobol_Code/app/bms/COUSR00.bms#L428) |
| COUSR0A | LNAME10 | 19 | 48 | 20 | ASKIP,FSET,NORM | BLUE |  |   | [L433](../Old_Cobol_Code/app/bms/COUSR00.bms#L433) |
| COUSR0A | UTYPE10 | 19 | 73 | 1 | ASKIP,FSET,NORM | BLUE |  |   | [L438](../Old_Cobol_Code/app/bms/COUSR00.bms#L438) |
| COUSR0A | ERRMSG | 23 | 1 | 78 | ASKIP,BRT,FSET | RED |  |  | [L449](../Old_Cobol_Code/app/bms/COUSR00.bms#L449) |

## COUSR01

Source: [app/bms/COUSR01.bms](../Old_Cobol_Code/app/bms/COUSR01.bms)

| Map | Field | Row | Column | Length | ATTRB | Color | Highlight | Initial | Source |
|---|---|---:|---:|---:|---|---|---|---|---|
| COUSR1A | TRNNAME | 1 | 7 | 4 | ASKIP,FSET,NORM | BLUE |  |  | [L34](../Old_Cobol_Code/app/bms/COUSR01.bms#L34) |
| COUSR1A | TITLE01 | 1 | 21 | 40 | ASKIP,FSET,NORM | YELLOW |  |  | [L38](../Old_Cobol_Code/app/bms/COUSR01.bms#L38) |
| COUSR1A | CURDATE | 1 | 71 | 8 | ASKIP,FSET,NORM | BLUE |  | mm/dd/yy | [L47](../Old_Cobol_Code/app/bms/COUSR01.bms#L47) |
| COUSR1A | PGMNAME | 2 | 7 | 8 | ASKIP,FSET,NORM | BLUE |  |  | [L57](../Old_Cobol_Code/app/bms/COUSR01.bms#L57) |
| COUSR1A | TITLE02 | 2 | 21 | 40 | ASKIP,FSET,NORM | YELLOW |  |  | [L61](../Old_Cobol_Code/app/bms/COUSR01.bms#L61) |
| COUSR1A | CURTIME | 2 | 71 | 8 | ASKIP,FSET,NORM | BLUE |  | hh:mm:ss | [L70](../Old_Cobol_Code/app/bms/COUSR01.bms#L70) |
| COUSR1A | FNAME | 8 | 18 | 20 | FSET,IC,NORM,UNPROT | GREEN | UNDERLINE |  | [L84](../Old_Cobol_Code/app/bms/COUSR01.bms#L84) |
| COUSR1A | LNAME | 8 | 56 | 20 | FSET,NORM,UNPROT | GREEN | UNDERLINE |  | [L97](../Old_Cobol_Code/app/bms/COUSR01.bms#L97) |
| COUSR1A | USERID | 11 | 15 | 8 | FSET,NORM,UNPROT | GREEN | UNDERLINE |  | [L111](../Old_Cobol_Code/app/bms/COUSR01.bms#L111) |
| COUSR1A | PASSWD | 11 | 55 | 8 | DRK,FSET,UNPROT | GREEN | UNDERLINE |  | [L126](../Old_Cobol_Code/app/bms/COUSR01.bms#L126) |
| COUSR1A | USRTYPE | 14 | 17 | 1 | FSET,NORM,UNPROT | GREEN | UNDERLINE |  | [L141](../Old_Cobol_Code/app/bms/COUSR01.bms#L141) |
| COUSR1A | ERRMSG | 23 | 1 | 78 | ASKIP,BRT,FSET | RED |  |  | [L151](../Old_Cobol_Code/app/bms/COUSR01.bms#L151) |

## COUSR02

Source: [app/bms/COUSR02.bms](../Old_Cobol_Code/app/bms/COUSR02.bms)

| Map | Field | Row | Column | Length | ATTRB | Color | Highlight | Initial | Source |
|---|---|---:|---:|---:|---|---|---|---|---|
| COUSR2A | TRNNAME | 1 | 7 | 4 | ASKIP,FSET,NORM | BLUE |  |  | [L34](../Old_Cobol_Code/app/bms/COUSR02.bms#L34) |
| COUSR2A | TITLE01 | 1 | 21 | 40 | ASKIP,FSET,NORM | YELLOW |  |  | [L38](../Old_Cobol_Code/app/bms/COUSR02.bms#L38) |
| COUSR2A | CURDATE | 1 | 71 | 8 | ASKIP,FSET,NORM | BLUE |  | mm/dd/yy | [L47](../Old_Cobol_Code/app/bms/COUSR02.bms#L47) |
| COUSR2A | PGMNAME | 2 | 7 | 8 | ASKIP,FSET,NORM | BLUE |  |  | [L57](../Old_Cobol_Code/app/bms/COUSR02.bms#L57) |
| COUSR2A | TITLE02 | 2 | 21 | 40 | ASKIP,FSET,NORM | YELLOW |  |  | [L61](../Old_Cobol_Code/app/bms/COUSR02.bms#L61) |
| COUSR2A | CURTIME | 2 | 71 | 8 | ASKIP,FSET,NORM | BLUE |  | hh:mm:ss | [L70](../Old_Cobol_Code/app/bms/COUSR02.bms#L70) |
| COUSR2A | USRIDIN | 6 | 21 | 8 | FSET,IC,NORM,UNPROT | GREEN | UNDERLINE |  | [L85](../Old_Cobol_Code/app/bms/COUSR02.bms#L85) |
| COUSR2A | FNAME | 11 | 18 | 20 | FSET,NORM,UNPROT | GREEN | UNDERLINE |  | [L103](../Old_Cobol_Code/app/bms/COUSR02.bms#L103) |
| COUSR2A | LNAME | 11 | 56 | 20 | FSET,NORM,UNPROT | GREEN | UNDERLINE |  | [L116](../Old_Cobol_Code/app/bms/COUSR02.bms#L116) |
| COUSR2A | PASSWD | 13 | 16 | 8 | DRK,FSET,UNPROT | GREEN | UNDERLINE |  | [L130](../Old_Cobol_Code/app/bms/COUSR02.bms#L130) |
| COUSR2A | USRTYPE | 15 | 17 | 1 | FSET,NORM,UNPROT | GREEN | UNDERLINE |  | [L145](../Old_Cobol_Code/app/bms/COUSR02.bms#L145) |
| COUSR2A | ERRMSG | 23 | 1 | 78 | ASKIP,BRT,FSET | RED |  |  | [L155](../Old_Cobol_Code/app/bms/COUSR02.bms#L155) |

## COUSR03

Source: [app/bms/COUSR03.bms](../Old_Cobol_Code/app/bms/COUSR03.bms)

| Map | Field | Row | Column | Length | ATTRB | Color | Highlight | Initial | Source |
|---|---|---:|---:|---:|---|---|---|---|---|
| COUSR3A | TRNNAME | 1 | 7 | 4 | ASKIP,FSET,NORM | BLUE |  |  | [L34](../Old_Cobol_Code/app/bms/COUSR03.bms#L34) |
| COUSR3A | TITLE01 | 1 | 21 | 40 | ASKIP,FSET,NORM | YELLOW |  |  | [L38](../Old_Cobol_Code/app/bms/COUSR03.bms#L38) |
| COUSR3A | CURDATE | 1 | 71 | 8 | ASKIP,FSET,NORM | BLUE |  | mm/dd/yy | [L47](../Old_Cobol_Code/app/bms/COUSR03.bms#L47) |
| COUSR3A | PGMNAME | 2 | 7 | 8 | ASKIP,FSET,NORM | BLUE |  |  | [L57](../Old_Cobol_Code/app/bms/COUSR03.bms#L57) |
| COUSR3A | TITLE02 | 2 | 21 | 40 | ASKIP,FSET,NORM | YELLOW |  |  | [L61](../Old_Cobol_Code/app/bms/COUSR03.bms#L61) |
| COUSR3A | CURTIME | 2 | 71 | 8 | ASKIP,FSET,NORM | BLUE |  | hh:mm:ss | [L70](../Old_Cobol_Code/app/bms/COUSR03.bms#L70) |
| COUSR3A | USRIDIN | 6 | 21 | 8 | FSET,IC,NORM,UNPROT | GREEN | UNDERLINE |  | [L85](../Old_Cobol_Code/app/bms/COUSR03.bms#L85) |
| COUSR3A | FNAME | 11 | 18 | 20 | ASKIP,FSET,NORM | BLUE | UNDERLINE |  | [L103](../Old_Cobol_Code/app/bms/COUSR03.bms#L103) |
| COUSR3A | LNAME | 13 | 18 | 20 | ASKIP,FSET,NORM | BLUE | UNDERLINE |  | [L116](../Old_Cobol_Code/app/bms/COUSR03.bms#L116) |
| COUSR3A | USRTYPE | 15 | 17 | 1 | ASKIP,FSET,NORM | BLUE | UNDERLINE |  | [L130](../Old_Cobol_Code/app/bms/COUSR03.bms#L130) |
| COUSR3A | ERRMSG | 23 | 1 | 78 | ASKIP,BRT,FSET | RED |  |  | [L140](../Old_Cobol_Code/app/bms/COUSR03.bms#L140) |

---

[Online screens](04-Online-Screens-and-Navigation.md) | [Home](Home.md) | [Program catalog](Appendix-Program-Catalog.md)
