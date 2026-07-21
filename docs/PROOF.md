# PersonalServer — Proof

Real runs of the shipped tools against my private experience vault, captured 2026-07-21 by driving
the published server over stdio (the same JSON-RPC a client like Claude Desktop sends).

**Redaction discipline:** employer, person, and location specifics are replaced with `[bracketed]`
placeholders. Everything that makes the harness *provable* is verbatim — tool output structure,
provenance ids, skill/tag aggregates, and **all metrics**. No vault files are committed; only these
redacted captures.

The thesis this page defends: the harness reads real career evidence and **never fabricates**. Counts
are computed, not guessed; every bullet is tied to a source id; metrics are preserved verbatim; and a
thin note yields a **gap question, not an invented number**.

---

## 1. Grounded STAR — `get_experience`

One experience = one markdown file; the tool returns the full grounded record. This note is confirmed
and high-confidence across all four beats (no redaction needed — it is about this repo):

```json
{
  "id": "2026-07-13-career-vault-harness",
  "title": "coalesced the STARfolio moat into an MCP server over my obsidian vault",
  "context": "project", "status": "confirmed",
  "confidence": { "situation": "high", "task": "high", "action": "high", "result": "high" },
  "skills": ["system design", "MCP server building", "technical judgment", "refactoring"],
  "tags": ["career", "architecture", "mcp", "obsidian", "decision"],
  "metrics": [{ "label": "wall-clock, STARfolio scrap -> harness shipped", "unit": "~1 month" }],
  "entities": ["[[STARfolio]]", "[[Obsidian]]", "[[PersonalServer]]", "[[MCP]]", "[[Claude]]"],
  "situation": "... i had PersonalServer, a C#/.NET stdio MCP server, that up to then basically just
                bridged the old STARfolio sqlite bank.",
  "task": "coalesce that IP into PersonalServer so the server itself is the successor to STARfolio,
           not a proxy to the frozen app. and do it vault-first ...",
  "action": "built a Vault/ module that reads my obsidian vault directly — one experience per markdown
             file, [[wikilinks]] as the graph for free, no db at all. embedded the grounded prompts
             verbatim ... shipped it as eight tools in one PR ...",
  "result": "honestly it's still theoretical until the vault actually has real experiences banked in
             it ... the machine's there, the output isn't yet.",
  "gaps": []
}
```

The `result` beat is candid rather than inflated — the tool records what happened, including "not
proven yet." That honesty is the point, and Section 4 shows the guard that enforces it.

---

## 2. Real aggregates — `find_tendencies`

The counts are computed across the whole vault at call time. The model is handed these as ground
truth with an instruction to *interpret, never invent or adjust a number*:

| aggregate | value |
|---|---|
| experiences | **18** |
| confirmed / draft | 2 / 16 |
| with metrics / without | 15 / 3 |
| open gaps | 26 |
| skill kinds | technical 74 · soft 11 · domain 10 |
| top skills (by evidence) | C ×2 · C# ×2 · SQL Server ×2 · system design ×2 · systems programming ×2 · technical judgment ×2 · Win32 ×2 |
| contexts | project 10 · work 4 · class 2 · other 2 |
| top tags | data-engineering 4 · systems 4 · concurrency 3 · internship 3 · research 3 |

18 experiences yield 62 distinct linked entities; the most-recurring is `[[Peekbar]]` (×3). The prompt
forbids naming any experience whose id is not in the stats — a claim can only be as specific as the
evidence.

---

## 3. Bullets with provenance — `tailor_bullets`

Given a job description (as **data**, never instructions), the tool returns every banked experience as
a block tagged with its `id`, plus the BULLETS prompt whose first rule is:

> Each bullet MUST be grounded in exactly one provided experience, and you MUST tag it with that
> experience's id in `experience_id`. Only use ids from the provided list. Never invent an experience,
> a fact, a company, or a metric. PRESERVE metrics verbatim ...

Driven with a backend-engineer JD (C#/.NET, SQL pipelines, AWS, secure APIs), the grounded bullets
Claude writes from those blocks each carry their source id and verbatim metrics (employers redacted):

- **[C#/.NET · secure API]** Built a C#/.NET helpdesk MCP server for [a manufacturer] with Microsoft
  Entra auth and a secure image-upload pipeline, grounding classification against **200+** taxonomy
  values and replacing a **>1,000,000-token-per-image** context cost with a bounded **50 MB** upload
  path. — `experience_id: 2026-06-royomartin-helpdesk-mcp`
- **[SQL pipeline · data]** Built a regex-based Python (Pandas/NumPy) ETL over **24 years** of flood
  reports at [a parish emergency-management office], surfacing high-risk zones that informed a **$3M**
  municipal waterworks project, and deployed **13+** WebEOC dashboards onto a centralized cloud system.
  — `experience_id: 2025-06-emergency-management-flood-etl`
- **[SQL Server · warehouse]** Built a SQL Server ELT pipeline and Kimball dimensional warehouse over
  **~200,000** legacy mainframe records spanning **20 semesters** for [a university] College of
  Engineering. — `experience_id: 2025-11-lsu-coe-elt-warehouse`

Every bullet traces back to one note; every number appears in that note's `metrics`. Nothing here was
invented — it was selected and tailored from grounded blocks.

---

## 4. Refusal to fabricate — `write_experience` → `confirm_experience`

The keystone. A deliberately thin note is banked — a vague situation, no action or result, and a gap
question. This ran against a throwaway temp vault (never the real one):

The note as written to disk — `metrics: []`, empty Action/Result, gap as a checkbox:

```yaml
---
id: thin-demo
title: "vague internship data task"
status: draft
metrics: []
---
## Situation
i think i helped speed up a data job at the internship but i do not remember the numbers.
## Action

## Result

## Gaps
- [ ] what was the concrete action you took, and what measurable outcome (runtime, %, records)
      resulted? no numbers yet on record.
```

Confirming it — asking the server to vouch the note as true — is **refused**, verbatim:

```json
{ "error": "cannot confirm: 1 open gap(s): what was the concrete action you took, and what measurable
   outcome (runtime, %, records) resulted? no numbers yet on record. | action beat is empty |
   result beat is empty" }
```

The tool does not invent a runtime, a percentage, or a record count to make the note look finished. It
names exactly what is missing and hands back the gap question. **Absent stays absent** — the grounding
discipline the whole harness is built on, enforced at the write boundary.

---

*Captured by driving `PersonalServer.exe` over stdio (`initialize` → `tools/call`) against a
local vault. Metrics and ids verbatim; employer / person / location specifics redacted; no vault data
committed.*
