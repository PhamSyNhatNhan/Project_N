"""Summarize actual fixture samples without selecting production policy limits."""
import json
import sys
from pathlib import Path

folder = Path(sys.argv[1] if len(sys.argv) > 1 else ".utmp/LifecyclePrototype")
samples = json.loads((folder / "NativeSamples.json").read_text(encoding="utf-8-sig"))
provenance = json.loads((folder / "Provenance.json").read_text(encoding="utf-8-sig"))
frames = json.loads((folder / "PlayFrames.json").read_text(encoding="utf-8-sig"))
assert len(frames["frames"]) == 180
assert provenance["residentPeak"] == 128
assert all(row["Milliseconds"] >= 0 for row in samples)
if not provenance["allocationCounterAvailable"]:
    assert all(row["AllocatedBytes"] == -1 for row in samples)

phases = ["load-total", "capture-existing-directory-and-buff", "encode-existing-buff-envelopes", "encode-prototype-actor-body", "unload-total"]
summary = []
for count in provenance["counts"]:
    for buffs in provenance["buffs"]:
        for phase in phases:
            rows = [r for r in samples if r["Actors"] == count and r["BuffsPerActor"] == buffs and r["Phase"] == phase]
            assert len(rows) == 3, (count, buffs, phase)
            for group in ["first-touch", "reload"]:
                selected = [r for r in rows if (r["Repeat"] == 0) == (group == "first-touch")]
                times = [r["Milliseconds"] for r in selected]
                summary.append(dict(actors=count, buffsPerActor=buffs, phase=phase, group=group, n=len(selected),
                                    minMs=min(times), maxMs=max(times),
                                    maximumBodyBytes=max(r["WireBytes"] for r in selected)))
frame_summary = {}
for key in ["wallMs", "mainThreadInclusiveMs", "waitForTargetFPSMs", "gcBytes"]:
    values = [r[key] for r in frames["frames"] if r[key] >= 0]
    frame_summary[key] = dict(validSamples=len(values), minimum=min(values) if values else None, maximum=max(values) if values else None)
result = dict(provenance=provenance, nativeTimingSamples=len(samples), nativeGroups=summary,
              authoredSceneReadyActors=frames["readyActors"], frameSummary=frame_summary,
              productionPolicySelected=False)
(folder / "BaselineSummary.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
lines = ["# Lifecycle baseline — bước 1", "", "Dữ liệu Unity Editor thật; chưa chọn policy production.", "",
         f"Unity {provenance['unity']}; {provenance['machine']}; RAM {provenance['memoryMiB']} MiB.",
         f"Fingerprint: `{provenance['contentFingerprint']}`. Không nạp code mod; RulesVersion66 / Mod API5.", "",
         f"Native: {len(samples)} timing samples, 1/16/64 actor, 0/4 Buff mỗi actor, 3 lượt mỗi case; resident control 128 object.",
         "Mix ở N≥16: 25% player Stat+Ability, 75% enemy Stat; TimeScale/Control/Buff, presentation headless. N=1 là player.", "",
         "| Actors | Buff/actor | Đường đo | Nhóm | n | Min ms | Max ms | Body bytes |",
         "| --- | --- | --- | --- | --- | --- | --- | --- |"]
for r in summary:
    if r["actors"] == 64:
        lines.append(f"| {r['actors']} | {r['buffsPerActor']} | {r['phase']} | {r['group']} | {r['n']} | {r['minMs']:.3f} | {r['maxMs']:.3f} | {r['maximumBodyBytes']} |")
lines += ["", "Play: 180 mẫu frame trên scene hiện tại, " + str(frames["readyActors"]) + " actor Ready; không phải swarm.",
          f"Wall min/max {frame_summary['wallMs']['minimum']:.3f}/{frame_summary['wallMs']['maximum']:.3f} ms; main-thread inclusive min/max {frame_summary['mainThreadInclusiveMs']['minimum']:.3f}/{frame_summary['mainThreadInclusiveMs']['maximum']:.3f} ms.", "",
          "Các giới hạn của số đo:", "",
          "- Timing step bọc module thật bằng subclass loader dành cho fixture; instrumentation có overhead.",
          "- Ba lượt/case tách first-touch n=1 và reload n=2; chỉ min/max, không median/p95/p99. Không gọi first-touch là asset-cold.",
          "- Capture lấy directory/Buff presentation hiện có; body mẫu bọc lại Buff JSON. Chưa chứa full Stat/Move/world state hoặc ranh giới commit mới.",
          "- Chi phí serialize Buff JSON được đo riêng; không dùng nó làm bound cho serializer production chưa thiết kế.",
          "- Native allocation counter calibrated: " + str(provenance["allocationCounterAvailable"]) + "; -1 nghĩa là unavailable, không phải không cấp phát.",
          "- Frame wall/main là inclusive, có thể gồm idle/editor; không trừ các marker chồng nhau để tạo active baseline giả.",
          "- 2N dùng loader hiện tại với identity riêng; chưa chứng minh Prepared/Commit, generation replacement hoặc pool production.",
          "- Chưa đo goodput, accepted native buffer, commit mới, Player/CoreCLR hoặc toàn bộ scene gameplay ở tải cao.",
          "- Đây là xử lý lại raw samples của lượt baseline trước, không phải lượt đo mới. Lượt Player cần warmup riêng, hàng trăm mẫu/case để báo p99; CoreCLR phải đo lại.",
          "", "Kết quả dùng để xác định đường cần đo lại khi bước 3–4 được triển khai; chưa đủ để chốt deadline/cap production.", ""]
(folder / "BaselineReport.md").write_text("\n".join(lines), encoding="utf-8")
print(f"PASS baseline summary validation: {len(samples)} native samples, {len(summary)} groups, 180 Play frames")
