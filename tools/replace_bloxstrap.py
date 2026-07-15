import os
import re

root = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
print(f"Repo root: {root}")

allowed_exts = {'.cs', '.csproj', '.sln', '.py', '.ps1', '.md', '.xaml', '.json', '.txt', '.manifest', '.resx', '.config', '.xml', '.yml', '.yaml', '.sh'}

skip_dir_parts = ['\\bin\\', '\\obj\\', '.git\\', '/bin/', '/obj/']

url_indicators = ['http://', 'https://', 'github.com', 'bloxstraplabs', 'raw.githubusercontent.com', 'img.shields.io']

patterns = [
    (re.compile(r'\bBloxstrap\b'), 'Leotrap'),
    (re.compile(r'\bbloxstrap\b'), 'leotrap'),
]

# additional simple replacements to catch punctuation-adjacent tokens
extra_replaces = [
    ('Leotrap-', 'Leotrap-'),
    ('leotrap-', 'leotrap-'),
    ('Leotrap.', 'Leotrap.'),
    ('leotrap.', 'leotrap.'),
    ('Leotrap/', 'Leotrap/'),
    ('leotrap/', 'leotrap/'),
]

changed_files = []

for dirpath, dirnames, filenames in os.walk(root):
    # skip hidden and build folders
    if any(part in dirpath for part in skip_dir_parts):
        continue

    for fname in filenames:
        fpath = os.path.join(dirpath, fname)
        _, ext = os.path.splitext(fname)
        if ext.lower() not in allowed_exts:
            continue

        try:
            with open(fpath, 'r', encoding='utf-8') as f:
                lines = f.readlines()
        except Exception:
            # skip files we can't read as text
            continue

        new_lines = []
        file_changed = False
        for line in lines:
            # if this line contains a URL or a known external host, skip replacements on this line
            if any(ind in line for ind in url_indicators):
                new_lines.append(line)
                continue

            original = line
            for pat, repl in patterns:
                line = pat.sub(repl, line)

            for a, b in extra_replaces:
                if a in line:
                    line = line.replace(a, b)

            if line != original:
                file_changed = True
            new_lines.append(line)

        if file_changed:
            try:
                with open(fpath, 'w', encoding='utf-8') as f:
                    f.writelines(new_lines)
                changed_files.append(os.path.relpath(fpath, root))
            except Exception as e:
                print(f"Failed to write {fpath}: {e}")

print(f"Modified {len(changed_files)} files:")
for p in changed_files:
    print(p)
