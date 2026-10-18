"""Build and publish a developer test build to the dev branch (never the live channel).

    python3 tools/make_dev.py --bin <mod out folder> --chtest <chtest.exe> --notes "what changed" [--publish]

Writes dist-dev/connected-hearts-dev.zip and dist-dev/build.txt (first line: the build id, then the notes).
The zip holds bin\\ (the mod, the runtime, the relay, the loader and chtest), ConnectedHearts.exe and ui\\; it
has no version.txt, so a dev install never changes the release version. With --publish, the two files are
committed to the dev branch under dev/ and pushed. Launchers with the Test tab read
<repo>/dev/dev/build.txt and <repo>/dev/dev/connected-hearts-dev.zip.
"""
import argparse, datetime, pathlib, shutil, subprocess, tempfile, zipfile

ROOT = pathlib.Path(__file__).resolve().parent.parent
BINARIES = ('connected_hearts.dll', 'winmm.dll', 'connected_hearts_runtime.exe', 'connected_hearts_relay.exe')


def git(*a, cwd=ROOT, check=True):
    return subprocess.run(['git', *a], cwd=cwd, check=check, capture_output=True, text=True).stdout.strip()


def next_id() -> str:
    day = datetime.date.today().strftime('%Y%m%d')
    prev = ''
    try:
        prev = git('show', 'origin/dev:dev/build.txt').splitlines()[0].strip()
    except Exception:
        pass
    n = int(prev.split('.')[1]) + 1 if prev.startswith(day + '.') else 1
    return f'{day}.{n}'


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument('--bin', required=True)
    ap.add_argument('--chtest', required=True)
    ap.add_argument('--notes', default='')
    ap.add_argument('--publish', action='store_true')
    a = ap.parse_args()
    git('fetch', 'origin', '+refs/heads/dev:refs/remotes/origin/dev', check=False)
    # The committed ConnectedHearts.exe is the live (release) launcher; the dev zip needs one built from this tree.
    subprocess.run(['sh', 'tools/build_exe.sh'], cwd=ROOT, check=True)
    build = next_id()
    out = ROOT / 'dist-dev'
    shutil.rmtree(out, ignore_errors=True)
    out.mkdir()
    zpath = out / 'connected-hearts-dev.zip'
    src = pathlib.Path(a.bin)
    with zipfile.ZipFile(zpath, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for f in BINARIES:
            z.write(src / f, 'bin/' + f)
        z.write(a.chtest, 'bin/chtest.exe')
        z.write(ROOT / 'ConnectedHearts.exe', 'ConnectedHearts.exe')
        for f in sorted((ROOT / 'ui').iterdir()):
            if f.is_file():
                z.write(f, 'ui/' + f.name)
    (out / 'build.txt').write_text(build + '\n' + a.notes.strip() + '\n', encoding='utf-8')
    for f in ('ConnectedHearts.exe', 'Setup.exe'):
        git('checkout', '--', f, check=False)
    print(build, zpath.stat().st_size, 'bytes')
    if not a.publish:
        return
    with tempfile.TemporaryDirectory() as tmp:
        wt = pathlib.Path(tmp) / 'dev'
        if git('ls-remote', '--heads', 'origin', 'dev'):
            git('worktree', 'add', '-f', str(wt), 'origin/dev')
            git('checkout', '-B', 'dev', cwd=wt)
        else:
            git('worktree', 'add', '-f', '--detach', str(wt))
            git('checkout', '--orphan', 'dev', cwd=wt)
            git('rm', '-rfq', '.', cwd=wt)
        (wt / 'dev').mkdir(exist_ok=True)
        shutil.copyfile(zpath, wt / 'dev' / 'connected-hearts-dev.zip')
        shutil.copyfile(out / 'build.txt', wt / 'dev' / 'build.txt')
        status = ROOT / 'tools' / 'DEV_STATUS.md'
        if status.exists():
            shutil.copyfile(status, wt / 'STATUS.md')
        git('add', '-A', cwd=wt)
        git('-c', 'user.name=JayKazma', '-c', 'user.email=diamondkid79@gmail.com', 'commit', '-qm', f'Dev build {build}', cwd=wt)
        git('push', 'origin', 'dev', cwd=wt)
        git('worktree', 'remove', '--force', str(wt))
    print('published', build)


if __name__ == '__main__':
    main()
