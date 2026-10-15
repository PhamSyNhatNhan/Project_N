"""Summarize matched burst diagnostics without pretending delayed timers are frame IDs."""
import argparse
import json
import statistics
import bisect
from collections import defaultdict
from pathlib import Path
import sys
sys.path.insert(0, str(Path(__file__).parent))
from SummarizeLifecyclePolicy import summarize, distribution


def analyze(folder):
    run = summarize(folder)
    frames = [g for g in run['groups'] if g['phase'] == 'frame']
    mutations = defaultdict(lambda: {'stackActors': 0, 'reapplyActors': 0})
    for line in (folder / 'Samples.jsonl').open(encoding='utf-8-sig'):
        row = json.loads(line)
        if row['Phase'] == 'stack-burst':
            mutations[row['Frame']]['stackActors'] += row['Actors']
        elif row['Phase'] == 'remove-apply-burst':
            mutations[row['Frame']]['reapplyActors'] += row['Actors']
    events = []
    gc_samples = defaultdict(list)
    for line in (folder / 'Samples.jsonl').open(encoding='utf-8-sig'):
        row = json.loads(line)
        if row['Phase'] == 'frame' and row['Sample'] >= 8 and 'GcCollections0' in row:
            gc_samples[row['Repeat']].append(row['GcCollections0'])
    gc_activity = {str(k): {'first': min(v), 'last': max(v), 'observedCollections': max(v)-min(v)} for k, v in gc_samples.items()}
    path = folder / 'Events.jsonl'
    if path.exists():
        prior_gc = None
        for line in path.open(encoding='utf-8-sig'):
            e = json.loads(line)
            current_gc = e.get('gcCollections0')
            e['gcSincePreviousLabel'] = current_gc-prior_gc if current_gc is not None and prior_gc is not None else None
            if current_gc is not None:
                prior_gc = current_gc
            if e['stage'] == 'steady' and e['sample'] >= 8:
                e['observedUpdateToLateMs'] = (e['lateTimestamp'] - e['updateTimestamp']) * 1000 / run['provenance']['stopwatchFrequency']
                e['dotCalls'] = e['counters'].get('dot.trigger', {}).get('Calls', 0)
                e['hitCalls'] = e['counters'].get('combat.receive', {}).get('Calls', 0)
                # This is an attempted popup call, not proof that a pool object was shown.
                e['popupRequests'] = e['counters'].get('combat.damage-popup', {}).get('Calls', 0)
                e['mutationActors'] = mutations[e['frame']]
                events.append(e)
    cycles = []
    for repeat in range(3):
        ticks = [e for e in events if e['repeat'] == repeat and e['dotCalls'] > 0]
        intervals = [b['gameSeconds'] - a['gameSeconds'] for a, b in zip(ticks, ticks[1:])]
        cycles.append({'repeat': repeat, 'tickFrames': len(ticks), 'dotCount': sum(e['dotCalls'] for e in ticks),
                       'tickFrameGameInterval': distribution(intervals),
                       'nearHalfSecondFraction': sum(abs(x - .5) < .06 for x in intervals) / len(intervals) if intervals else None})
    scoped = {}
    for e in events:
        for name, sample in e['counters'].items():
            if name not in scoped:
                scoped[name] = {'calls': 0, 'inclusiveBytes': 0, 'inclusiveMs': 0, 'maxFrameBytes': 0, 'maxFrameMs': 0}
            s = scoped[name]
            s['calls'] += sample['Calls']
            s['inclusiveBytes'] += sample.get('AllocatedBytes', 0)
            s['inclusiveMs'] += sample.get('Milliseconds', 0)
            s['maxFrameBytes'] = max(s['maxFrameBytes'], sample.get('AllocatedBytes', 0))
            s['maxFrameMs'] = max(s['maxFrameMs'], sample.get('Milliseconds', 0))
    if not run['provenance'].get('allocationAvailable') or not run['provenance'].get('allocationTrace'):
        for s in scoped.values():
            s['inclusiveBytes'] = s['maxFrameBytes'] = None
    joined = []
    native = sorted((e for e in events if e.get('nativeUpdateTimestamp', 0) > 0), key=lambda e: e['nativeUpdateTimestamp'])
    if native:
        updates = [e['nativeUpdateTimestamp'] for e in native]
        observations = {}
        for line in (folder / 'Samples.jsonl').open(encoding='utf-8-sig'):
            row = json.loads(line)
            if row['Phase'] == 'frame' and row['Sample'] >= 8 and row['TimingTimestamp'] > 0:
                observations[row['TimingTimestamp']] = row
        timestamps = sorted(observations)
        for a, b in zip(timestamps, timestamps[1:]):
            i, j = bisect.bisect_left(updates, a), bisect.bisect_left(updates, b)
            # A missing timing observation may span multiple Unity frames: never guess.
            if j - i != 1 or native[i]['nativeLateTimestamp'] >= b:
                continue
            e = native[i]
            row = observations[a]
            joined.append({'unityFrame': e['frame'], 'nativeTimingStart': a, 'observedByUnityFrame': row['Frame'],
                           'repeat': e['repeat'], 'gameSeconds': e['gameSeconds'], 'realSeconds': e['realtimeSeconds'],
                           'activeMainMs': row['ActiveMainMs'], 'dotCalls': e['dotCalls'], 'hitCalls': e['hitCalls'],
                           'popupRequests': e['popupRequests'], 'wireBytes': e['wireBytes'], 'messages': e['messages'],
                           'gcSincePreviousLabel': e['gcSincePreviousLabel'],
                           'mutationActors': e['mutationActors']})
    return {'folder': str(folder), 'provenance': run['provenance'], 'result': run['result'],
            'frames': frames, 'cycles': cycles, 'scopes': scoped, 'gcActivity': gc_activity,
            'gcCaveat': 'Cumulative CollectionCount changes between samples indicate collection activity, not pause duration. Label intervals run between LateUpdates and include work outside Update/Late scopes; delayed native timing requires QPC matching. Missing historical counters are unavailable.',
            'topObservedWindows': sorted(events, key=lambda e: e['observedUpdateToLateMs'], reverse=True)[:30],
            'topTickFrames': sorted(events, key=lambda e: e['dotCalls'], reverse=True)[:30],
            'joinedFrames': len(joined), 'joinedPeaks': sorted(joined, key=lambda e: e['activeMainMs'], reverse=True)[:100],
            'joinedDotFrameMs': distribution([e['activeMainMs'] for e in joined if e['dotCalls']]),
            'joinedGcObservedFrames': sum((e['gcSincePreviousLabel'] or 0) > 0 for e in joined),
            'joinedDotWithGcObservedMs': distribution([e['activeMainMs'] for e in joined if e['dotCalls'] and (e['gcSincePreviousLabel'] or 0) > 0]),
            'joinedDotWithoutGcObservedMs': distribution([e['activeMainMs'] for e in joined if e['dotCalls'] and e['gcSincePreviousLabel'] == 0]),
            'joinedNonDotFrameMs': distribution([e['activeMainMs'] for e in joined if not e['dotCalls']]),
            'timingJoin': 'Native QPC join only when exactly one labelled Update/Late frame lies within adjacent native timing starts; ambiguous/missing intervals excluded.' if native else 'Unjoined: Mono stopwatch and native FrameTiming timestamps have different origins. Equal timer frequencies do not establish an origin. Scope counts are exact observed Unity frames; update-to-late spans omit earlier/later work. Do not assign a delayed FrameTiming spike to its observer frame.'}


def main():
    p = argparse.ArgumentParser()
    p.add_argument('folders', type=Path, nargs='+')
    p.add_argument('--output', type=Path, required=True)
    args = p.parse_args()
    data = [analyze(f) for f in args.folders]
    args.output.mkdir(parents=True, exist_ok=True)
    (args.output / 'Analysis.json').write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding='utf-8')
    lines = ['# Matched Buff burst investigation', '', 'All repeats capped120FPS. Labels and Development traces have diagnostic overhead; unlabelled normal controls are the frame-performance evidence.', '',
             '| Run | Repeat | n | p50 ms | p99 ms | Max ms | >8.333 ms | Longest late sequence |', '|---|---:|---:|---:|---:|---:|---:|---:|']
    for run in data:
        for g in run['frames']:
            d = g['activeMainMs']
            lines.append(f"| {Path(run['folder']).name} | {g['repeat']} | {d['n']} | {d.get('p50')} | {d.get('p99')} | {d.get('max')} | {g['activeMainOver8_33Fraction']} | {g['longestObservedLateFrameRun']} |")
        lines += ['', f"{Path(run['folder']).name}: clean={run['result']['cleanSuccess']}; timer correlation: {run['timingJoin']}", '']
    (args.output / 'Report.md').write_text('\n'.join(lines), encoding='utf-8')
    print(json.dumps({'runs': len(data), 'allClean': all(x['result']['cleanSuccess'] for x in data)}))


if __name__ == '__main__':
    main()
