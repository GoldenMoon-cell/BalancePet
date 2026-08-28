from __future__ import annotations

import ctypes
import subprocess
import sys
import time
from ctypes import wintypes
from pathlib import Path


BASE_DIR = Path(sys.executable).resolve().parent if getattr(sys, "frozen", False) else Path(__file__).resolve().parent
PET_EXE = BASE_DIR / "BalancePet.exe"
CODEX_NAMES = {"CHATGPT.EXE", "CODEX.EXE"}
PET_NAME = "BalancePet.exe"
CREATE_NO_WINDOW = getattr(subprocess, "CREATE_NO_WINDOW", 0)
TH32CS_SNAPPROCESS = 0x00000002
INVALID_HANDLE_VALUE = ctypes.c_void_p(-1).value
LOG_PATH = BASE_DIR / "BalancePetWatcher.log"


def log(message: str) -> None:
    try:
        LOG_PATH.write_text(f"{time.strftime('%H:%M:%S')} {message}\n", encoding="utf-8", errors="replace")
    except OSError:
        pass


class PROCESSENTRY32W(ctypes.Structure):
    _fields_ = [
        ("dwSize", wintypes.DWORD),
        ("cntUsage", wintypes.DWORD),
        ("th32ProcessID", wintypes.DWORD),
        ("th32DefaultHeapID", ctypes.POINTER(ctypes.c_ulong)),
        ("th32ModuleID", wintypes.DWORD),
        ("cntThreads", wintypes.DWORD),
        ("th32ParentProcessID", wintypes.DWORD),
        ("pcPriClassBase", ctypes.c_long),
        ("dwFlags", wintypes.DWORD),
        ("szExeFile", wintypes.WCHAR * 260),
    ]


def process_rows() -> list[dict[str, str]]:
    snapshot = ctypes.windll.kernel32.CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0)
    if snapshot == INVALID_HANDLE_VALUE:
        return []
    rows: list[dict[str, str]] = []
    entry = PROCESSENTRY32W()
    entry.dwSize = ctypes.sizeof(entry)
    try:
        ok = ctypes.windll.kernel32.Process32FirstW(snapshot, ctypes.byref(entry))
        while ok:
            rows.append({"image": entry.szExeFile, "pid": str(entry.th32ProcessID)})
            ok = ctypes.windll.kernel32.Process32NextW(snapshot, ctypes.byref(entry))
    finally:
        ctypes.windll.kernel32.CloseHandle(snapshot)
    return rows


def codex_running(rows: list[dict[str, str]]) -> bool:
    return any((row.get("image") or "").strip().upper() in CODEX_NAMES for row in rows)


def pet_running(rows: list[dict[str, str]]) -> bool:
    return any((row.get("image") or "").strip().lower() == PET_NAME.lower() for row in rows)


def start_pet() -> None:
    log(f"start_pet exists={PET_EXE.exists()} base={BASE_DIR}")
    if PET_EXE.exists():
        subprocess.Popen([str(PET_EXE)], cwd=BASE_DIR, creationflags=CREATE_NO_WINDOW)


def stop_pet(rows: list[dict[str, str]]) -> None:
    for row in rows:
        if (row.get("image") or "").strip().lower() != PET_NAME.lower():
            continue
        pid = (row.get("pid") or "").strip()
        if pid.isdigit():
            subprocess.run(
                ["taskkill", "/PID", pid, "/T", "/F"],
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                creationflags=CREATE_NO_WINDOW,
                timeout=3,
            )


def main() -> None:
    while True:
        try:
            rows = process_rows()
            current_codex = codex_running(rows)
            current_pet = pet_running(rows)
            log(f"codex={current_codex} pet={current_pet} rows={len(rows)}")
            if current_codex and not current_pet:
                start_pet()
            elif not current_codex and current_pet:
                stop_pet(rows)
        except Exception as error:
            log(f"error={type(error).__name__}: {error}")
        time.sleep(3)


if __name__ == "__main__":
    main()
