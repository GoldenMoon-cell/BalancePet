# -*- mode: python ; coding: utf-8 -*-

from pathlib import Path

project_dir = Path(__file__).resolve().parent

a = Analysis(
    [str(project_dir / 'balance_pet_qt.py')],
    pathex=[],
    binaries=[],
    datas=[(str(project_dir / 'assets'), 'assets')],
    hiddenimports=[],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[str(project_dir / 'pyinstaller' / 'qt_runtime_hook.py')],
    excludes=[],
    noarchive=False,
    optimize=0,
)
pyz = PYZ(a.pure)

exe = EXE(
    pyz,
    a.scripts,
    [],
    exclude_binaries=True,
    name='BalancePet',
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=False,
    console=False,
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
    icon=[str(project_dir / 'assets' / 'balance-pet.ico')],
)
coll = COLLECT(
    exe,
    a.binaries,
    a.datas,
    strip=False,
    upx=False,
    upx_exclude=[],
    name='BalancePet',
)
