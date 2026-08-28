# -*- mode: python ; coding: utf-8 -*-


a = Analysis(
    ['C:\\Users\\GoldenMoon\\Desktop\\BalancePet\\versions\\python\\balance_pet_qt.py'],
    pathex=[],
    binaries=[],
    datas=[('C:\\Users\\GoldenMoon\\Desktop\\BalancePet\\versions\\python\\assets', 'assets')],
    hiddenimports=[],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=['C:\\Users\\GoldenMoon\\Desktop\\BalancePet\\versions\\python\\pyinstaller\\qt_runtime_hook.py'],
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
    icon=['C:\\Users\\GoldenMoon\\Desktop\\BalancePet\\versions\\python\\assets\\balance-pet.ico'],
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
