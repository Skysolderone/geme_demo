# Journal - rubioc (Part 1)

> AI development session journal
> Started: 2026-09-12

---


## 2026-09-16 — denser-map 200-match regression

**Map v2 (`siege-4p-base-v2`, 85 playable / 36 obstacles) achieved its stated goals but exposed an AI calibration defect.**

Same seeds (1–200), 4 players, Standard AI, round cap 15.

| metric | v1 + catch-up (`sim-out/cur-regress`) | v2 (`sim-out/denser-map-200`) | v2 + Safety=5 (`sim-out/safety5`) |
|---|---|---|---|
| first capture (major round) | 6.52 | 5.46 | 4.68 |
| matches with zero captures | — | 60 / 200 | 3 / 200 |
| non-convergence (MajorRoundLimit) | 11.5% | 27.5% | 9.5% |
| finished-match length | 11.15 | 9.43 | 10.23 |
| round-3 leader win rate | 46.5% | 64.5% | 51.0% |

**Root cause of the non-convergence spike: `EvaluationWeights.Default.Safety = 20`.**
That value was tuned on the sparse v1 map and its own doc-comment flags it as "阶段 B 的首个校准项" (stage-B's first calibration item). Sweep on v2 (200 matches each, `sim-out/safety*`):

| Safety | 5 | 10 | 20 | 30 | 40 | 60 |
|---|---|---|---|---|---|---|
| non-convergence | 9.5% | 31.5% | 27.5% | 34.0% | 42.0% | 57.5% |

Non-monotonic; raising it makes things worse. High Safety keeps a score-improving move always available (patching liberties on threatened groups), so the AI never passes. In the 29 matches that stayed unresolved even at a 40-round cap, rounds 31–40 averaged 40.6 placements against 40.6 captures — net zero, concentrated on ~18 distinct cells.

**Snowball root cause is different from what catch-up-recruit assumed.** By rank, from major round 5 (120 matches): deploy limit ~5 for everyone, actual placements 1.61 / 0.92 / 0.68 / 0.75 for ranks 1–4, pass rate 22.9% / 42.7% / 52.8% / 45.2%. Nobody comes close to their deploy limit. Trailing players do not lack pieces, they lack worthwhile cells — so recruit-side compensation cannot reach them on a dense map. Compensation must grant space/position, not cards.

**Follow-ups queued**: (1) calibrate `Safety` as its own change with a 3/5/7/8 sweep — it shifts every existing baseline; (2) rework catch-up compensation toward space; (3) `maps/siege-4p-base-v1.json` no longer loads (109 playable vs the new 80–95 rule) yet §3.3 says it is kept for comparison; (4) `CommandLine` silently drops unknown options — `--matches` was accepted and ignored, running 1 match instead of 200.
