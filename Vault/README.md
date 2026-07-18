# Vault — the career harness

The durable IP of the frozen STARfolio app, coalesced into PersonalServer as tools over a plain
Obsidian-style markdown vault. No app, no DB for this half — one experience = one `.md` file, and
`[[wikilinks]]` + backlinks are the knowledge graph for free. This module is independent of
`superstar.db` and STARfolio's loopback; it is the successor, not a proxy.

## The moat, embedded

Two disciplines make the output trustworthy, and they live here verbatim in `Prompts.cs`:

1. **Grounding** — never invent a fact, number, employer, outcome, or metric. Absent beats stay
   thin + low confidence + a gap question, never self-filled. Metrics preserved verbatim.
2. **ensureCited** — every non-terminal interview question cites ≥1 real corpus chunk. Enforced,
   not merely prompted: `check_citation` validates cited chunk_ids against the real vault.

A third, added here: **tendency-finding** grounded in real aggregates — the counts are computed off
the whole vault; the agent interprets patterns but cannot invent a number.

The LLM runs in the calling agent (Claude Desktop), not the server. So each tool assembles the
grounded prompt + the real corpus/aggregates and hands them back; the agent executes them.

## Tools

| tool | does |
|---|---|
| `bank_experience` | returns the matching grounded extractor (notes/resume/evidence) + entity prompt + STAR schema + write rules + known vault entities |
| `write_experience` | commits a grounded STAR note to `experiences/` (frontmatter + beats + gap todos), metrics verbatim, entities as `[[wikilinks]]` |
| `update_experience` | patches a note by id; only the fields you pass change (per-beat text/confidence, skills/tags/metrics/entities/gaps as list replacements). answer a gap by filling its beat and passing the trimmed gaps list |
| `confirm_experience` | flips `draft -> confirmed` by id; refuses (and reports why) if action/result is empty or low-confidence or gaps remain |
| `search_experiences` | free-text search over the vault (title/beats/tags/skills/entities), ranked, with snippets |
| `query_experiences` | structured filter by skills / tags / context / status → experience cards |
| `get_experience` | fetch one full note by id (frontmatter + beats + metrics + entities + gaps) |
| `neighbors` | experiences connected via a shared `[[wikilink]]` entity or a shared skill — the graph, emergent from backlinks |
| `tailor_bullets` | vault as tagged blocks + the BULLETS prompt → JD-tailored resume bullets, each grounded to one experience id |
| `generate_story` | named experiences + behavioral-answer prompt + voice rules → a grounded spoken interview answer, provenance to their ids |
| `debrief_interview` | interview transcript (wrapped as DATA) + debrief prompt + optional experiences → grounded feedback, strengths, gaps, and reconstructed STAR stories; no fabrication |
| `find_tendencies` | real cross-vault aggregates (skill/tag/context/entity freq, metric coverage, gaps, draft vs confirmed) + the tendency prompt |
| `expertise_profile` | skills weighted by evidence count (+ ids), metric-carrying results, recurring domains + the expertise prompt |
| `defend_repo` | the person's experiences as cited corpus chunks (`id#beat`) + interviewer prompt + ensureCited invariant; optional topic scope |
| `check_citation` | hard guard: are these chunk_ids real? lists unknown ones |
| `voice_guide` | voice rules + recent notes as fresh style samples, for writing anything back in the person's voice |

## Vault location

`EXPERIENCE_VAULT` env var, else `~/Documents/Design_Exp`. Experiences under `experiences/*.md`;
entity notes are the vault-root `*.md` files that `[[wikilinks]]` resolve to. Missing vault →
tools return a clear error string, never throw over stdio.

## Note format

Frontmatter: `id, title, context, status, confidence{per-beat}, skills[{name,kind}], tags[],
metrics[{label,value,unit}], entities[]`. Body: `## Situation/Task/Action/Result` beats, `## Gaps`
as `- [ ]` todos. `id` is a stable slug — it's what bullets cite; don't rename after they reference it.

Auto-discovered via `[McpServerToolType]`; no `Program.cs` wiring needed.
