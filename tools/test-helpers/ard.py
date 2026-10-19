# ard.py [TARGET]: the current room's area data on that game (default the host): every map/btl/evt program and
# what it calls (spawn groups such as b_40, missions such as TT20_MS603). A boss's warp is
# --map/--btl/--evt with the program whose btl spawns the boss's group (khbr locations.yaml names the groups).
import json, time, glob, os, sys, struct
here = os.path.dirname(os.path.abspath(__file__)); target = sys.argv[1] if len(sys.argv) > 1 else '1'
root = os.path.abspath(os.path.join(here, '..', '..'))
def run(args, wait=4):
    t = f"{int(time.time()*1000)}_ard"; json.dump({"cmd": "chtest", "args": args, "target": target}, open(os.path.join(here, 'inbox', t + '.json'), 'w'))
    end = time.time() + wait
    while time.time() < end:
        r = glob.glob(os.path.join(here, 'results', t + '_*.json'))
        if r: time.sleep(0.1); return json.load(open(r[0]))
        time.sleep(0.1)
r = run("peek --rva 0x2A0F828:u64,0x717008:u8,0x717009:u8")
s = r['samples'][0]; ard = int(s['0x2A0F828'], 16)
print(f"world {s['0x717008']:02X} room {s['0x717009']:02X} area data at {ard:#x}")
run(f"test dump {ard:#x} 0x200 1 100 ard_hdr.bin")
hdr = open(os.path.join(root, 'ard_hdr.bin'), 'rb').read()
n, base = struct.unpack_from('<II', hdr, 4)
ents = []
for i in range(n):
    typ = struct.unpack_from('<H', hdr, 0x10 + 0x10 * i)[0]; name = hdr[0x14 + 0x10 * i:0x18 + 0x10 * i].split(b'\0')[0].decode('ascii', 'replace')
    off, size = struct.unpack_from('<II', hdr, 0x18 + 0x10 * i); ents.append((typ, name, ard + off - base, size))
print('entries:', ' '.join(f"{nm}" for _, nm, _, _ in ents))
for typ, name, addr, size in ents:
    if typ != 13 or size > 0x4000: continue
    run(f"test dump {addr:#x} {size:#x} 1 100 ard_{name}.bin")
    b = open(os.path.join(root, f'ard_{name}.bin'), 'rb').read(); o = 0
    print('==', name)
    while o + 4 <= len(b):
        pid, sz = struct.unpack_from('<HH', b, o)
        if pid == 0xFFFF or sz < 4: break
        body = b[o + 4:o + sz]; i = 0; calls = []
        while i + 4 <= len(body):
            op, cnt = struct.unpack_from('<HH', body, i); p = body[i + 4:i + 4 + 4 * cnt]
            texts = [t.decode() for t in p.split(b'\0') if len(t) >= 4 and t.isascii() and t[:1].isalpha()]
            if texts: calls.append(f"{op}:{'/'.join(texts)}")
            i += 4 + 4 * cnt
        if calls: print(f"  program {pid} ({pid:#x})  {' '.join(calls)}")
        o += sz
