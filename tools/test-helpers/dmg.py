# dmg.py NAME=HP [NAME=HP ...]: host damage calls bringing each named boss down to HP (one call each).
import json, time, glob, sys, os
here = os.path.dirname(os.path.abspath(__file__))
def run(c, tag, w=2.5):
    t = f"{int(time.time()*1000)}_{tag}"; json.dump(c, open(os.path.join(here, 'inbox', t + '.json'), 'w')); time.sleep(w)
    r = glob.glob(os.path.join(here, 'results', t + '_*.json')); return json.load(open(r[0])) if r else None
want = dict(a.split('=') for a in sys.argv[1:])
e = run({"cmd": "chtest", "args": "entities", "target": "1"}, 'de', 2)
for a in (e or {}).get('actors', []):
    if a['name'] in want and a['hp'] > int(want[a['name']]):
        r = run({"cmd": "chtest", "args": f"hit damage --victim {a['address']} --amount {a['hp'] - int(want[a['name']])}", "target": "1"}, 'dd', 2)
        print(a['name'], r and (r.get('hpBefore'), r.get('hpAfter')), flush=True)
