# hunt.py TARGET ROUNDS [NAME]: that game's player goes next to each surfaced enemy (or only NAME) in turn and attacks it.
import json, time, glob, os, sys
here = os.path.dirname(os.path.abspath(__file__)); target = sys.argv[1] if len(sys.argv) > 1 else '2'
rounds = int(sys.argv[2]) if len(sys.argv) > 2 else 6
def send(c, tag):
    p = os.path.join(here, 'inbox', f"{int(time.time()*1000)}_{tag}.json"); json.dump(c, open(p, 'w')); return os.path.basename(p)[:-5]
def run(args, tag, wait=3.0):
    t = send({"cmd": "chtest", "args": args, "target": target}, tag); time.sleep(wait)
    r = glob.glob(os.path.join(here, 'results', t + '_*.json'))
    return json.load(open(r[0])) if r else None
for k in range(rounds):
    ents = run('entities', f'h{k}e')
    if not ents or 'actors' not in ents: print('no entities'); continue
    me = [a for a in ents['actors'] if a['objectType'] == 0 and a['name'].startswith('P_')][0]
    foes = [a for a in ents['actors'] if a['objectType'] in (3, 4, 12, 21) and a['hp'] > 0 and (len(sys.argv) < 4 or a['name'] == sys.argv[3])
            and a['motionId'] not in (161, 162)]
    if not foes: print('no surfaced enemies'); time.sleep(2); continue
    f = foes[k % len(foes)]; p = f['position']
    addr = int(me['address'], 16) + 0x670
    r = run(f"poke --addr {hex(addr)} --type f32x3 --value {p['x']+60:.1f},{p['y']:.1f},{p['z']:.1f}", f'h{k}p', 1.0)
    print('to', f['name'], f['address'][-6:], 'hp', f['hp'], 'poke', r and r.get('ok'), flush=True)
    for i in range(5): send({"cmd": "chtest", "args": "player-press --button cross --duration-ms 150", "target": target}, f'h{k}a{i}'); time.sleep(0.45)
    time.sleep(1)
