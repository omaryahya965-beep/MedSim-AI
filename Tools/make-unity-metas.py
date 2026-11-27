#!/usr/bin/env python3
"""Create missing Unity .meta files (deterministic GUIDs) for files/folders under the given Assets paths.
Run only for NEW files created outside the Unity editor, so every teammate gets the same GUIDs on pull.
Usage: python3 Tools/make-unity-metas.py Assets/MedSimAI/Surgery [more paths...]"""
import hashlib, os, sys

IMPORTERS = {
    '.cs': "MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n",
    '.asmdef': "AssemblyDefinitionImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n",
}
TEXT = "TextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
FOLDER = "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
DEFAULT = "DefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"

def guid(path): return hashlib.md5(("medsim-surgery:" + path.replace(os.sep, '/')).encode()).hexdigest()

def write(path, body):
    meta = path + '.meta'
    if os.path.exists(meta): return 0
    with open(meta, 'w', newline='\n') as f: f.write(f"fileFormatVersion: 2\nguid: {guid(path)}\n{body}")
    return 1

n = 0
for root in sys.argv[1:]:
    # the root folder itself
    if os.path.isdir(root): n += write(root.rstrip('/'), FOLDER)
    for d, dirs, files in os.walk(root):
        dirs[:] = [x for x in dirs if not x.startswith('.') and not x.endswith('~')]
        for x in dirs: n += write(os.path.join(d, x), FOLDER)
        for f in files:
            if f.endswith('.meta') or f.startswith('.'): continue
            ext = os.path.splitext(f)[1].lower()
            body = IMPORTERS.get(ext) or (TEXT if ext in ('.json', '.md', '.txt', '.csv', '.xml', '.bytes') else DEFAULT)
            n += write(os.path.join(d, f), body)
print(f"created {n} .meta files")
