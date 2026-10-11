"""Portable asset utilities; these do not install, activate, or regenerate a pet."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_ATLAS = ROOT / 'pets/zili-gpt-pet/spritesheet.png'
CELL = (192, 208)
ROWS = [('idle',6),('running-right',8),('running-left',8),('waving',4),
        ('jumping',5),('failed',8),('waiting',6),('running',6),('review',6),
        ('look-a',8),('look-b',8)]
DURATIONS = {
    'idle':[280,110,110,140,140,320],
    'running-right':[120]*7+[220], 'running-left':[120]*7+[220],
    'waving':[140]*3+[280], 'jumping':[140]*4+[280],
    'failed':[140]*7+[240], 'waiting':[150]*5+[260],
    'running':[120]*5+[220], 'review':[150]*5+[280],
}

def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()

def crop(atlas: Image.Image, row: int, col: int) -> Image.Image:
    return atlas.crop((col*192,row*208,(col+1)*192,(row+1)*208))

def validate(atlas_path: Path, check_manifest: bool) -> dict:
    errors=[]
    with Image.open(atlas_path) as source:
        if source.format != 'PNG': errors.append('Atlas must be encoded as PNG.')
        if source.mode != 'RGBA': errors.append('Atlas must have RGBA channels.')
        atlas=source.convert('RGBA')
    if atlas.size != (1536,2288): errors.append('v2 atlas must be exactly 1536x2288.')
    if atlas_path.stat().st_size > 20*1024*1024: errors.append('Atlas exceeds 20 MiB.')
    used=unused=0
    if atlas.size == (1536,2288):
        for row,(state,count) in enumerate(ROWS):
            for col in range(8):
                present=crop(atlas,row,col).getchannel('A').getbbox() is not None
                if col<count:
                    used+=1
                    if not present: errors.append(f'{state} frame {col} is empty.')
                else:
                    unused+=1
                    if present: errors.append(f'{state} unused cell {col} is not transparent.')
    checked=0
    if check_manifest:
        manifest=json.loads((ROOT/'verification/artifact-manifest.json').read_text(encoding='utf-8'))
        for entry in manifest['files']:
            relative=Path(entry['path'])
            if relative.is_absolute() or '..' in relative.parts:
                errors.append('Unsafe manifest path.'); continue
            path=ROOT/relative
            if not path.is_file(): errors.append(f'Missing artifact: {relative.as_posix()}')
            elif digest(path)!=entry['sha256']: errors.append(f'Hash mismatch: {relative.as_posix()}')
            checked+=1
    return {'ok':not errors,'dimensions':list(atlas.size),'used_cells':used,'unused_cells':unused,
            'sha256':digest(atlas_path),'checked_manifest_files':checked,'errors':errors,
            'scope':'Basic distribution integrity; not a replacement for native acceptance or visual animation review.'}

def extract(atlas_path: Path, output: Path) -> None:
    atlas=Image.open(atlas_path).convert('RGBA')
    if atlas.size!=(1536,2288): raise ValueError('Expected a 1536x2288 v2 atlas.')
    for row,(state,count) in enumerate(ROWS):
        folder=output/state;folder.mkdir(parents=True,exist_ok=True)
        for col in range(count): crop(atlas,row,col).save(folder/f'{col:02d}.png')

def compose(frames: Path, output: Path) -> None:
    # Load every required frame before writing output; preserve source cell size.
    cells=[]
    for row,(state,count) in enumerate(ROWS):
        for col in range(count):
            with Image.open(frames/state/f'{col:02d}.png') as im:
                if im.size!=CELL: raise ValueError(f'{state}/{col:02d}.png must be 192x208.')
                cells.append((row,col,im.convert('RGBA')))
    atlas=Image.new('RGBA',(1536,2288))
    for row,col,im in cells: atlas.paste(im,(col*192,row*208))
    output.parent.mkdir(parents=True,exist_ok=True)
    atlas.save(output)

def gif(frames: list, durations: list, path: Path) -> None:
    frames[0].save(path,save_all=True,append_images=frames[1:],duration=durations,
                   loop=0,disposal=2,optimize=False)

def preview(atlas_path: Path, output: Path) -> None:
    atlas=Image.open(atlas_path).convert('RGBA')
    if atlas.size!=(1536,2288): raise ValueError('Expected a 1536x2288 v2 atlas.')
    output.mkdir(parents=True,exist_ok=True)
    for row,(state,count) in enumerate(ROWS[:9]):
        gif([crop(atlas,row,c)for c in range(count)],DURATIONS[state],output/f'{state}.gif')
    idle=[crop(atlas,0,c)for c in range(6)]
    jump=[crop(atlas,4,c)for c in range(5)]
    slow_idle=[duration*6 for duration in DURATIONS['idle']]
    gif(idle+jump*3+idle,slow_idle+DURATIONS['jumping']*3+slow_idle,
        output/'native-idle-jump3-idle.gif')
    look=[crop(atlas,row,c)for row in (9,10)for c in range(8)]
    gif(look,[200]*16,output/'look-loop.gif')

def main() -> None:
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command',choices=['validate','extract','compose','preview'])
    parser.add_argument('--atlas',type=Path,default=DEFAULT_ATLAS)
    parser.add_argument('--output',type=Path)
    parser.add_argument('--frames',type=Path,default=ROOT/'build/frames')
    parser.add_argument('--skip-manifest',action='store_true',help='Skip original release hashes when checking a modified atlas.')
    args=parser.parse_args()
    if args.command=='validate':
        result=validate(args.atlas,not args.skip_manifest)
        print(json.dumps(result,ensure_ascii=False,indent=2))
        raise SystemExit(0 if result['ok'] else 1)
    elif args.command=='extract': extract(args.atlas,args.output or ROOT/'build/frames')
    elif args.command=='preview': preview(args.atlas,args.output or ROOT/'build/previews')
    else: compose(args.frames,args.output or ROOT/'build/spritesheet.png')
    print('Completed. Output is local; no account or network operation was performed.')

if __name__=='__main__':main()
