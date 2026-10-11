"""Build connected-hearts-update.zip for a release.

    python tools/make_release.py --bin <folder with connected_hearts.dll, winmm.dll, connected_hearts_runtime.exe> --version 0.1.1 --notes "what changed"

Writes dist/connected-hearts-update.zip (the launchers' update), dist/ConnectedHearts.zip (the same package, the
first-install download), dist/notes.txt and version.txt (repo root). Commit and push them; launchers read
version.txt, dist/notes.txt and dist/connected-hearts-update.zip from the main branch.
"""
import argparse, hashlib, json, pathlib, shutil, zipfile

ROOT = pathlib.Path(__file__).resolve().parent.parent
BINARIES = ('connected_hearts.dll', 'winmm.dll', 'connected_hearts_runtime.exe')


def digest(p: pathlib.Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument('--bin', required=True)
    ap.add_argument('--version', required=True)
    ap.add_argument('--notes', default='')
    ap.add_argument('--out', default=str(ROOT / 'dist' / 'connected-hearts-update.zip'))
    a = ap.parse_args()
    src = pathlib.Path(a.bin)
    stage = ROOT / 'dist' / 'stage'
    shutil.rmtree(stage, ignore_errors=True)
    (stage / 'bin').mkdir(parents=True)
    for f in BINARIES:
        shutil.copyfile(src / f, stage / 'bin' / f)
    if (src / 'connected_hearts_relay.exe').exists():   # local relay for the solo test (optional)
        shutil.copyfile(src / 'connected_hearts_relay.exe', stage / 'bin' / 'connected_hearts_relay.exe')
    for f in ('ConnectedHearts.exe', 'ConnectedHearts.exe.config'):
        shutil.copyfile(ROOT / f, stage / f)
    shutil.copyfile(ROOT / 'tools' / 'PACKAGE_README.md', stage / 'README.md')   # no repository links in the package
    (stage / 'ui').mkdir()
    for f in (ROOT / 'ui').iterdir():
        if f.is_file():
            shutil.copyfile(f, stage / 'ui' / f.name)
    if (ROOT / 'lib').is_dir():   # WebView2 wrappers for the frameless window (optional; Edge app window otherwise)
        shutil.copytree(ROOT / 'lib', stage / 'lib')
    (stage / 'licenses').mkdir()
    for f in (ROOT / 'licenses').iterdir():
        shutil.copyfile(f, stage / 'licenses' / f.name)
    (stage / 'CONNECTED-HEARTS-PACKAGE').write_text('connected-hearts-package-v1\n', encoding='ascii')
    (stage / 'version.txt').write_text(a.version + '\r\n', encoding='ascii')
    files = {p.relative_to(stage).as_posix(): digest(p) for p in sorted(stage.rglob('*')) if p.is_file()}
    manifest = {'schema': 1, 'name': 'Connected Hearts (Steam)', 'version': a.version, 'avatarBridgeVersion': 3,
                'protocol': 10, 'gameBuild': '1.0.0.10-steam-global', 'content': 'none', 'mod': 'none',
                'supportedGame': {'fileVersion': '1.0.0.2', 'edition': 'Steam Global'},
                'products': {'loader': 'bin/winmm.dll', 'inject': 'bin/connected_hearts.dll',
                             'runtime': 'bin/connected_hearts_runtime.exe'},
                'files': files}
    (stage / 'package.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    out = pathlib.Path(a.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    if out.exists():
        out.unlink()
    with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for p in sorted(stage.rglob('*')):
            if p.is_file():
                z.write(p, p.relative_to(stage).as_posix())
    shutil.copyfile(out, out.parent / 'ConnectedHearts.zip')
    (ROOT / 'version.txt').write_text(a.version + '\n', encoding='ascii')
    (out.parent / 'notes.txt').write_text(a.notes + '\n', encoding='utf-8')
    shutil.rmtree(stage, ignore_errors=True)
    print(out, out.stat().st_size, 'bytes')


if __name__ == '__main__':
    main()
