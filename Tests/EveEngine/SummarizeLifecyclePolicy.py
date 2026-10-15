"""Validate and summarize independent Player runs; never turn empty markers into zero."""
import argparse
import json
import math
from collections import defaultdict
from pathlib import Path


def distribution(values):
    values = sorted(v for v in values if isinstance(v, (int, float)) and math.isfinite(v) and v >= 0)
    if not values:
        return {"n": 0, "available": False}
    result = {"n": len(values), "min": values[0], "max": values[-1], "mean": sum(values) / len(values)}
    # Nearest rank. Small samples are intentionally not dressed up as tail estimates.
    for label, fraction, minimum in [("p50", .5, 20), ("p95", .95, 100), ("p99", .99, 200)]:
        if len(values) >= minimum:
            result[label] = values[max(0, math.ceil(fraction * len(values)) - 1)]
    return result


def summarize(folder):
    provenance = json.loads((folder / "Provenance.json").read_text(encoding="utf-8-sig"))
    result = json.loads((folder / "Result.json").read_text(encoding="utf-8-sig")) if (folder / "Result.json").exists() else {"success": False, "incomplete": True}
    errors_path = folder / "Errors.txt"
    runtime_log_present = errors_path.exists() and errors_path.stat().st_size > 0
    # Older fixture completion did not include caught/logged behavior exceptions. Never call it a clean PASS.
    result["legacyCompletionOrCompleted"] = result.get("completed", result.get("success", False))
    result["runtimeLogPresent"] = runtime_log_present
    result["cleanSuccess"] = result.get("success", False) and not runtime_log_present
    groups = defaultdict(list)
    for line in (folder / "Samples.jsonl").open(encoding="utf-8-sig"):
        row = json.loads(line)
        for key in ["Ms", "BudgetMs", "WallMs"]:
            if not math.isfinite(row[key]) or row[key] < 0:
                raise ValueError(f"Invalid {key}: {row}")
        key = (row["Case"], row["Pattern"], row["BudgetMs"], row["Repeat"], row["Phase"], row["Step"], "first-touch" if row["Sample"] < 0 else "measured")
        groups[key].append(row)
    output = []
    for key, rows in groups.items():
        entry = {"case": key[0], "pattern": key[1], "budgetMs": key[2], "repeat": key[3], "phase": key[4], "step": key[5], "condition": key[6],
                 "ms": distribution([r["Ms"] for r in rows]), "residentMax": max(r["Resident"] for r in rows), "backlogMax": max(r["Backlog"] for r in rows),
                 "wireBytes": sum(r["WireBytes"] for r in rows), "messages": sum(r["Messages"] for r in rows)}
        if key[4] == "frame":
            # FrameTiming is delayed: repeated timestamp is the same observation, not another sample.
            timing_rows = rows[8:]  # Drain delayed timings from the preceding phase after Capture starts.
            unique = {r["TimingTimestamp"]: r for r in timing_rows if r["TimingTimestamp"] > 0}
            over = longest = 0
            for row in unique.values():
                over = over + 1 if row["ActiveMainMs"] > 1000 / 120 else 0
                longest = max(longest, over)
            gpu = [r["GpuFrameMs"] for r in unique.values()]
            seconds = sum(r["WallMs"] for r in rows) / 1000
            byte_rate = sum(r["WireBytes"] for r in rows) / seconds if seconds else None
            entry.update({"wallMs": distribution([r["WallMs"] for r in rows]),
                          "activeMainMs": distribution([r["ActiveMainMs"] for r in unique.values()]),
                          "gpuMs": distribution(gpu) if any(x > 0 for x in gpu) else {"n": 0, "available": False, "reason": "No positive GPU observations; zero is not a full-game GPU bound"},
                          "presentWaitMs": distribution([r["PresentWaitMs"] for r in unique.values()]),
                          "gcBytes": distribution([r["GcBytes"] for r in rows]),
                          "managedBytes": distribution([r["ManagedBytes"] for r in rows]),
                          "nativeAllocatedBytes": distribution([r["NativeAllocatedBytes"] for r in rows if "NativeAllocatedBytes" in r]),
                          "gameDeltaMs": distribution([r["GameDeltaMs"] for r in rows if "GameDeltaMs" in r]),
                          "timingUnique": len(unique), "timingDuplicatesOrUnavailable": len(rows) - len(unique),
                          "timingTransitionSamplesExcluded": min(8, len(rows)),
                          "activeMainOver8_33Fraction": sum(r["ActiveMainMs"] > 1000 / 120 for r in unique.values()) / len(unique) if unique else None,
                          "longestObservedLateFrameRun": longest,
                          "maximumFrameWireBytes": max(r["WireBytes"] for r in rows),
                          "maximumFrameMessages": max(r["Messages"] for r in rows),
                          "wireBytesPerSecondAllPeers": byte_rate,
                          "wireBytesPerSecondPerPeer": byte_rate / provenance.get("peerCount", 3) if byte_rate is not None and provenance.get("peerCount", 3) > 0 else None})
            game_seconds = sum(r.get("GameDeltaMs", 0) for r in rows) / 1000
            entry["wireBytesPerGameSecondAllPeers"] = sum(r["WireBytes"] for r in rows) / game_seconds if game_seconds else None
        output.append(entry)
    return {"folder": str(folder.resolve()), "provenance": provenance, "result": result, "groups": output}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("folders", nargs="+", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    runs = [summarize(folder) for folder in args.folders]
    args.output.mkdir(parents=True, exist_ok=True)
    (args.output / "Summary.json").write_text(json.dumps(runs, indent=2, ensure_ascii=False), encoding="utf-8")
    lines = ["# Lifecycle policy Player measurements", "", "No production policy selected. FrameTiming active-main field is reported directly; inclusive Main Thread/wall counters are not substituted for it.",
             "Normal Player instrumentation is retained in totals. No enemy AI, complete character rendering, Move/HP replication, or real transport is covered. Projectile combination uses the existing bounded synthetic collision fixture.", ""]
    for run in runs:
        lines += [f"## {run['provenance']['mode']} — {run['folder']}", "", f"Result: {run['result']}", "", "| Case | Pattern | Budget | Repeat | Phase | n | p99 ms | Max ms |", "|---|---|---:|---:|---|---:|---:|---:|"]
        for group in run["groups"]:
            if group["step"] or group["phase"] in ["create-object", "begin-prepare", "prepare-next", "outbox-encode-send"]:
                continue
            dist = group.get("activeMainMs", group["ms"])
            lines.append(f"| {group['case']} | {group['pattern']} | {group['budgetMs']} | {group['repeat']} | {group['phase']} ({group['condition']}) | {dist['n']} | {dist.get('p99', '—')} | {dist.get('max', '—')} |")
    (args.output / "Report.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(json.dumps({"runs": len(runs), "groups": sum(len(r["groups"]) for r in runs), "allCleanSuccessful": all(r["result"].get("cleanSuccess") for r in runs)}))


if __name__ == "__main__":
    main()
