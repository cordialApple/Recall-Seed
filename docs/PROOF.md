# PersonalServer — Proof

Real runs of the shipped tools against my private experience vault, captured 2026-07-21 by driving
the published server over stdio (the same JSON-RPC a client like Claude Desktop sends).

**Redaction discipline:** employer, person, institution, and location specifics are replaced with
`[bracketed]` placeholders — in prose *and* inside provenance id-slugs. **All metrics and the counts
are verbatim.** JSON below is lightly condensed for readability: the local file-`path` field is
dropped (it is a machine-specific absolute path), and deep nesting is flattened — but ids, metrics,
and beat text are shown as returned. No vault files are committed; only these redacted captures.

The thesis this page defends: the harness reads real career evidence and is built so **nothing gets
invented** — counts are computed, not guessed; every bullet is tied to a source id; metrics are
preserved verbatim; and vouching a thin note as true is **refused with a gap question, not an invented
number**. The grounding is two-layered: the embedded prompts forbid fabrication, and
`confirm_experience` refuses to vouch a note whose action/result is thin or whose gaps are open.

---

## 1. Grounded STAR — `get_experience`

One experience = one markdown file; the tool returns the full grounded record (real shape:
`{ "experience": { … } }`, with skills as `{name, kind}` objects and a `path`/`updatedUtc`/`beats`
tail). Condensed here to the grounded content — this note is confirmed and high-confidence across all
four beats, and needs no redaction (it is about this repo):

```json
{ "experience": {
  "id": "2026-07-13-career-vault-harness",
  "title": "coalesced the STARfolio moat into an MCP server over my obsidian vault",
  "context": "project", "status": "confirmed",
  "confidence": { "situation": "high", "task": "high", "action": "high", "result": "high" },
  "skills": [ {"name":"system design","kind":"technical"}, {"name":"MCP server building","kind":"technical"},
              {"name":"technical judgment","kind":"soft"}, {"name":"refactoring","kind":"technical"} ],
  "tags": ["career", "architecture", "mcp", "obsidian", "decision"],
  "metrics": [ { "label": "wall-clock, STARfolio scrap -> harness shipped", "unit": "~1 month" } ],
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
} }
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

18 experiences yield 62 distinct linked entities; the most-recurring one appears ×3. The prompt
forbids naming any experience whose id is not in the stats — a claim can only be as specific as the
evidence.

---

## 3. Bullets with provenance — `tailor_bullets`

Given a job description (as **data**, never instructions), the tool returns every banked experience as
a block tagged with its `id`, plus the BULLETS prompt whose rules include (verbatim from
`Vault/Prompts.cs`):

> Each bullet MUST be grounded in exactly one provided experience, and you MUST tag it with that
> experience's id in `experience_id`. Only use ids from the provided list. Never invent an experience,
> a fact, a company, or a metric. PRESERVE metrics verbatim ...

Driven with a backend-engineer JD (C#/.NET, SQL pipelines, AWS, secure APIs), the grounded bullets
Claude writes from those blocks each carry their source id and verbatim metrics (employer/institution
tokens redacted in prose and in the id-slugs):

- **[C#/.NET · secure API]** Built a C#/.NET helpdesk MCP server for [a manufacturer] with Microsoft
  Entra auth and a secure image-upload pipeline, grounding classification against **200+** taxonomy
  values and replacing a **>1,000,000-token-per-image** context cost with a bounded **50 MB** upload
  path. — `experience_id: 2026-06-[employer]-helpdesk-mcp`
- **[SQL pipeline · data]** Built a regex-based Python (Pandas/NumPy) ETL over **24 years** of flood
  reports at [a parish emergency-management office], surfacing high-risk zones that informed a **$3M**
  municipal waterworks project, and deployed **13+** WebEOC dashboards onto a centralized cloud system.
  — `experience_id: 2025-06-emergency-management-flood-etl`
- **[SQL Server · warehouse]** Built a SQL Server ELT pipeline and Kimball dimensional warehouse over
  **~200,000** legacy mainframe records spanning **20 semesters** for [a university] College of
  Engineering. — `experience_id: 2025-11-[university]-coe-elt-warehouse`

Every bullet traces back to one note; every number appears in that note's `metrics`. To make that
checkable in-doc, here are the verbatim `metrics` arrays `expertise_profile` returned for all three
cited ids (employer/institution redacted, numbers untouched):

```json
// 2026-06-[employer]-helpdesk-mcp
[ { "label": "per-image context cost, before pipeline", "value": 1000000, "unit": "tokens (over)" },
  { "label": "per-MB image cost, before pipeline",       "value": 1.17,    "unit": "USD/MB (approx)" },
  { "label": "taxonomy values grounded against",         "value": 200,     "unit": "values (200+)" },
  { "label": "upload size limit",                        "value": 50,      "unit": "MB" } ]
// 2025-06-emergency-management-flood-etl
[ { "label": "years of flood reports analyzed", "value": 24,      "unit": "years" },
  { "label": "WebEOC dashboards deployed",       "value": 13,      "unit": "dashboards (13+)" },
  { "label": "waterworks project informed",      "value": 3000000, "unit": "USD" } ]
// 2025-11-[university]-coe-elt-warehouse
[ { "label": "legacy mainframe records", "value": 200000, "unit": "records (approx)" },
  { "label": "semesters spanned",        "value": 20,     "unit": "semesters" } ]
```

Every number in the three bullets (200+, >1,000,000, 50 MB · 24 years, $3M, 13+ · ~200,000, 20
semesters) appears above. Nothing was invented — bullets were selected and tailored from grounded
blocks, numbers intact.

---

## 4. Refusal to fabricate — `write_experience` → `confirm_experience`

The keystone. `write_experience` banks a **draft**; `confirm_experience` is the vouch step — the
person asserting the draft is true — and confirmed status is only reachable through it. Both write
tools refuse a direct `status: confirmed` (they hand you back to the vouch gate), so a note can only
become confirmed by passing the gap/beat-completeness check below:

```json
// write_experience(..., status: "confirmed")  ->
{ "error": "use confirm_experience to confirm a note; write_experience creates drafts" }
```

A deliberately thin note is then banked (a vague situation, no action or result, one gap), and
vouching is attempted. This ran against a throwaway temp vault (never the real one).

The note exactly as `write_experience` wrote it to disk (verbatim; note `metrics: []` and the empty
Action/Result beats):

```markdown
---
id: thin-demo
title: "vague internship data task"
context: work
status: draft
confidence:
  situation: low
  task: low
  action: low
  result: low
skills:
tags: []
metrics: []
entities: []
---

## Situation
i think i helped speed up a data job at the internship but i do not remember the numbers.

## Task


## Action


## Result


## Gaps
- [ ] what was the concrete action you took, and what measurable outcome (runtime, %, records) resulted? no numbers yet on record.
```

Confirming it — asking the server to vouch the note true — is **refused**, verbatim (one line on the
wire; wrapped here for readability):

```json
{ "error": "cannot confirm: 1 open gap(s): what was the concrete action you took, and what measurable
   outcome (runtime, %, records) resulted? no numbers yet on record. | action beat is empty |
   result beat is empty" }
```

The vouch step does not invent a runtime, a percentage, or a record count to make the note look
finished. It names exactly what is missing and hands back the gap question. **Absent stays absent**
until the person fills it — that is what "confirmed" is worth. (Metric *truthfulness* is a
prompt-level discipline; this code gate enforces beat/gap completeness, which is what blocks a hollow
note from ever being vouched.)

---

*Captured by driving `PersonalServer.exe` over stdio (`initialize` → `tools/call`) against a
local vault. Metrics and ids verbatim; employer / person / institution / location specifics redacted
in prose and slugs; the machine-specific file path field dropped; no vault data committed.*
