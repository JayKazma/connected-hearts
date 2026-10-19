# Skip the current cutscene on the host (Start, Down, Cross); does nothing when no cutscene is playing.
import json, time, os, glob
here = os.path.dirname(os.path.abspath(__file__))
def send(a, t='1'):
    p = os.path.join(here, 'inbox', f"{int(time.time()*1000)}_s.json"); json.dump({"cmd": "chtest", "args": a, "target": t}, open(p, 'w')); time.sleep(1.2)
def cut():
    t = f"{int(time.time()*1000)}_sc"; json.dump({"cmd": "chtest", "args": "peek --rva 0x0B65210:i32", "target": "1"}, open(os.path.join(here, 'inbox', t + '.json'), 'w')); time.sleep(1.0)
    r = glob.glob(os.path.join(here, 'results', t + '_*.json'))
    try: return json.load(open(r[0]))['samples'][0]['0xB65210'] if r else None
    except Exception: return None
if not cut(): print('no cutscene playing'); raise SystemExit
send('player-press --button start --duration-ms 120'); send('player-press --button down --duration-ms 120'); send('player-press --button cross --duration-ms 120')
time.sleep(3)
