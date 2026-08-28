"""Make PySide6's sibling DLL directories visible before importing QtCore."""

import os
import sys
from pathlib import Path


if sys.platform == "win32" and getattr(sys, "frozen", False):
    root = Path(getattr(sys, "_MEIPASS", Path(sys.executable).parent))
    dll_dirs = [root / "PySide6", root / "shiboken6", root]
    for directory in dll_dirs:
        if directory.is_dir():
            try:
                os.add_dll_directory(str(directory))
            except (AttributeError, OSError):
                pass
    os.environ["PATH"] = os.pathsep.join(str(path) for path in dll_dirs if path.is_dir()) + os.pathsep + os.environ.get("PATH", "")
