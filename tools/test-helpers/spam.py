# spam.py SECONDS [TARGET]: alternate Cross and Triangle on one game.
import json, time, os, sys
here = os.path.dirname(os.path.abspath(__file__)); end = time.time() + float(sys.argv[1]); t = sys.argv[2] if len(sys.argv) > 2 else '2'; i = 0
while time.time() < end:
    b = 'triangle' if i % 3 == 2 else 'cross'; i += 1
    json.dump({"cmd": "chtest", "args": f"player-press --button {b} --duration-ms 100", "target": t}, open(os.path.join(here, 'inbox', f"{int(time.time()*1000)}_sp.json"), 'w')); time.sleep(0.35)
