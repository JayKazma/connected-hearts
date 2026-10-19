# cmp.py N: sample the boss (types 3/12) on both games N times; position, motion, HP.
import json, time, glob, os, sys
here = os.path.dirname(os.path.abspath(__file__))
for k in range(int(sys.argv[1])):
    t = f"{int(time.time()*1000)}_cmp"; json.dump({"cmd": "chtest", "args": "entities", "target": "both"}, open(os.path.join(here, 'inbox', t + '.json'), 'w')); time.sleep(2.2)
    row = []
    for r in sorted(glob.glob(os.path.join(here, 'results', t + '_*.json'))):
        pid = r.rsplit('_', 1)[1][:-5]
        try: acts = json.load(open(r))['actors']
        except Exception: continue
        b = [a for a in acts if a['objectType'] in (3, 12)]
        me = [a for a in acts if a['objectType'] == 0][:1]
        s = f"{pid}: " + (" ".join(f"{a['name']} ({a['position']['x']:.0f},{a['position']['y']:.0f},{a['position']['z']:.0f}) m{a.get('motionId')} hp{a['hp']}" for a in b) or 'no boss')
        if me: s += f" | me m{me[0].get('motionId')} ({me[0]['position']['x']:.0f},{me[0]['position']['y']:.0f},{me[0]['position']['z']:.0f})"
        row.append(s)
    print(' || '.join(row), flush=True)
