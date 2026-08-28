from __future__ import annotations

import base64
import ctypes
import json
import queue
import sys
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
from ctypes import wintypes
from dataclasses import asdict, dataclass
from pathlib import Path
import tkinter as tk
from tkinter import messagebox, ttk

from balance_provider import BalanceSnapshot, GenericJsonProvider
from balance_provider import parse_amount as provider_parse_amount
from balance_provider import read_json_path as provider_read_json_path


APP_DIR = Path(sys.executable).resolve().parent if getattr(sys, "frozen", False) else Path(__file__).resolve().parent
CONFIG_PATH = APP_DIR / "balance-pet.json"
WINDOW_BG = "#1f2937"
TRANSPARENT = "#010101"


@dataclass
class Settings:
    endpoint: str = ""
    auth_mode: str = "bearer"
    token_blob: str = ""
    balance_path: str = "data.balance"
    currency: str = "USD"
    refresh_seconds: int = 300
    low_threshold: float = 5.0
    pet_style: str = "deepseek"
    pet_scale: float = 1.0
    window_x: int = -1
    window_y: int = -1
    flipped: bool = False
    interaction_mode: str = "free"


def _dpapi_transform(data: bytes, decrypt: bool = False) -> bytes:
    """Protect a token with Windows DPAPI, scoped to the current user."""
    if not hasattr(ctypes, "windll"):
        raise RuntimeError("Windows DPAPI is unavailable")

    class DATA_BLOB(ctypes.Structure):
        _fields_ = [("cbData", wintypes.DWORD), ("pbData", ctypes.POINTER(ctypes.c_byte))]

    raw = (ctypes.c_byte * len(data)).from_buffer_copy(data)
    source = DATA_BLOB(len(data), raw)
    target = DATA_BLOB()
    fn = ctypes.windll.crypt32.CryptUnprotectData if decrypt else ctypes.windll.crypt32.CryptProtectData
    ok = fn(ctypes.byref(source), None, None, None, None, 0, ctypes.byref(target))
    if not ok:
        raise ctypes.WinError()
    try:
        return ctypes.string_at(target.pbData, target.cbData)
    finally:
        ctypes.windll.kernel32.LocalFree(target.pbData)


def protect_token(token: str) -> str:
    if not token:
        return ""
    return base64.b64encode(_dpapi_transform(token.encode("utf-8"))).decode("ascii")


def unprotect_token(blob: str) -> str:
    if not blob:
        return ""
    return _dpapi_transform(base64.b64decode(blob), decrypt=True).decode("utf-8")


def load_settings() -> Settings:
    if not CONFIG_PATH.exists():
        return Settings()
    try:
        raw = json.loads(CONFIG_PATH.read_text(encoding="utf-8"))
        allowed = {field for field in Settings.__dataclass_fields__}
        values = {key: value for key, value in raw.items() if key in allowed}
        return Settings(**values)
    except (OSError, ValueError, TypeError):
        return Settings()


def save_settings(settings: Settings, token: str) -> None:
    values = asdict(settings)
    values["token_blob"] = protect_token(token)
    CONFIG_PATH.write_text(json.dumps(values, ensure_ascii=False, indent=2), encoding="utf-8")


def read_path(payload: object, path: str) -> object:
    return provider_read_json_path(payload, path)


def parse_amount(value: object) -> float:
    return provider_parse_amount(value)


_BALANCE_PROVIDER = GenericJsonProvider(unprotect_token)


def fetch_snapshot(settings: Settings) -> BalanceSnapshot:
    return _BALANCE_PROVIDER.fetch(settings)


def fetch_balance(settings: Settings) -> float:
    """Keep the existing Qt/fallback API while using the provider layer."""
    return fetch_snapshot(settings).amount


class PetApp:
    def __init__(self) -> None:
        self.settings = load_settings()
        self.events: queue.Queue[tuple[str, object]] = queue.Queue()
        self.root = tk.Tk()
        self.root.title("小余额")
        self.root.geometry("290x270+80+100")
        self.root.resizable(False, False)
        self.root.overrideredirect(True)
        self.root.configure(bg=TRANSPARENT)
        self.root.wm_attributes("-topmost", True)
        try:
            self.root.wm_attributes("-transparentcolor", TRANSPARENT)
        except tk.TclError:
            self.root.configure(bg=WINDOW_BG)

        self.canvas = tk.Canvas(self.root, width=290, height=270, bg=TRANSPARENT, highlightthickness=0)
        self.canvas.pack(fill="both", expand=True)
        self.canvas.bind("<ButtonPress-1>", self.start_drag)
        self.canvas.bind("<B1-Motion>", self.drag)
        self.canvas.bind("<Double-Button-1>", lambda _event: self.open_settings())
        self.canvas.bind("<Button-3>", self.show_menu)
        self.menu = tk.Menu(self.root, tearoff=False)
        self.menu.add_command(label="立即刷新", command=self.refresh)
        self.menu.add_command(label="配置接口", command=self.open_settings)
        self.menu.add_separator()
        self.menu.add_command(label="退出", command=self.root.destroy)

        self.phase = 0
        self.drag_offset = (0, 0)
        self.status = "准备就绪"
        self.balance: float | None = None
        self.last_updated = ""
        self.refresh_after_id: str | None = None
        self.draw_pet()
        self.root.after(100, self.process_events)
        self.root.after(180, self.animate)
        self.root.after(500, self.refresh)

    def start_drag(self, event: tk.Event) -> None:
        self.drag_offset = (event.x, event.y)

    def drag(self, event: tk.Event) -> None:
        x = self.root.winfo_x() + event.x - self.drag_offset[0]
        y = self.root.winfo_y() + event.y - self.drag_offset[1]
        self.root.geometry(f"+{x}+{y}")

    def show_menu(self, event: tk.Event) -> None:
        self.menu.tk_popup(event.x_root, event.y_root)

    def animate(self) -> None:
        self.phase = (self.phase + 1) % 24
        self.draw_pet()
        self.root.after(180, self.animate)

    def draw_pet(self) -> None:
        self.canvas.delete("all")
        bob = 2 if self.phase % 12 in (3, 4, 5, 6) else 0
        blink = self.phase % 24 in (20, 21)
        status_color = "#34d399" if self.balance is not None and self.balance > self.settings.low_threshold else "#fbbf24"
        if self.balance is None and self.status not in ("准备就绪", "刷新中..."):
            status_color = "#fb7185"

        self.canvas.create_oval(16, 15, 274, 263, fill="#111827", outline="", stipple="gray50")
        self.canvas.create_oval(35, 30 + bob, 255, 245 + bob, fill="#f8fafc", outline="#cbd5e1", width=2)
        self.canvas.create_oval(70, 44 + bob, 220, 118 + bob, fill="#e0f2fe", outline="")
        self.canvas.create_oval(82, 95 + bob, 208, 220 + bob, fill="#bae6fd", outline="#7dd3fc", width=2)
        self.canvas.create_oval(62, 53 + bob, 91, 91 + bob, fill="#38bdf8", outline="#0284c7", width=2)
        self.canvas.create_oval(199, 53 + bob, 228, 91 + bob, fill="#38bdf8", outline="#0284c7", width=2)
        if blink:
            self.canvas.create_line(101, 118 + bob, 117, 118 + bob, fill="#0f172a", width=3)
            self.canvas.create_line(173, 118 + bob, 189, 118 + bob, fill="#0f172a", width=3)
        else:
            self.canvas.create_oval(102, 108 + bob, 118, 126 + bob, fill="#0f172a", outline="")
            self.canvas.create_oval(172, 108 + bob, 188, 126 + bob, fill="#0f172a", outline="")
        self.canvas.create_arc(124, 122 + bob, 166, 155 + bob, start=200, extent=140, style="arc", outline="#0f172a", width=3)

        self.canvas.create_oval(184, 25, 268, 68, fill="#0f172a", outline="")
        amount = "--" if self.balance is None else f"{self.balance:,.2f}"
        self.canvas.create_text(226, 39, text=amount, fill="#f8fafc", font=("Segoe UI", 12, "bold"))
        self.canvas.create_text(226, 56, text=self.settings.currency.upper(), fill="#93c5fd", font=("Segoe UI", 8, "bold"))
        self.canvas.create_oval(228, 221, 244, 237, fill=status_color, outline="#ffffff", width=2)
        self.canvas.create_text(145, 244, text=self.status, fill="#334155", font=("Segoe UI", 9, "bold"))
        if self.last_updated:
            self.canvas.create_text(145, 258, text=self.last_updated, fill="#64748b", font=("Segoe UI", 7))

    def refresh(self) -> None:
        if self.status == "刷新中...":
            return
        if self.refresh_after_id is not None:
            try:
                self.root.after_cancel(self.refresh_after_id)
            except tk.TclError:
                pass
        self.refresh_after_id = None
        self.status = "刷新中..."
        self.draw_pet()
        threading.Thread(target=self._fetch_worker, daemon=True).start()

    def _fetch_worker(self) -> None:
        try:
            amount = fetch_balance(self.settings)
            self.events.put(("ok", amount))
        except Exception as exc:  # network and provider-specific errors belong in the UI
            self.events.put(("error", str(exc)))

    def process_events(self) -> None:
        handled = False
        try:
            while True:
                kind, payload = self.events.get_nowait()
                handled = True
                if kind == "ok":
                    self.balance = float(payload)
                    self.status = "余额已更新"
                    self.last_updated = time.strftime("%H:%M:%S")
                    if self.balance <= self.settings.low_threshold:
                        self.status = "余额偏低"
                else:
                    self.status = "刷新失败"
                    self.last_updated = str(payload)[:42]
                self.draw_pet()
        except queue.Empty:
            pass
        if handled and self.refresh_after_id is None:
            delay = max(30, int(self.settings.refresh_seconds)) * 1000
            self.refresh_after_id = self.root.after(delay, self.refresh)
        self.root.after(100, self.process_events)

    def open_settings(self) -> None:
        dialog = tk.Toplevel(self.root)
        dialog.title("小余额配置")
        dialog.resizable(False, False)
        dialog.transient(self.root)
        dialog.grab_set()
        frame = ttk.Frame(dialog, padding=14)
        frame.grid(sticky="nsew")
        fields: dict[str, tk.Variable] = {
            "endpoint": tk.StringVar(value=self.settings.endpoint),
            "auth_mode": tk.StringVar(value=self.settings.auth_mode),
            "token": tk.StringVar(value=""),
            "balance_path": tk.StringVar(value=self.settings.balance_path),
            "currency": tk.StringVar(value=self.settings.currency),
            "refresh_seconds": tk.StringVar(value=str(self.settings.refresh_seconds)),
            "low_threshold": tk.StringVar(value=str(self.settings.low_threshold)),
        }
        labels = [
            ("余额 API 地址", "endpoint"),
            ("认证方式", "auth_mode"),
            ("令牌（留空保持不变）", "token"),
            ("余额 JSON 路径", "balance_path"),
            ("货币显示", "currency"),
            ("刷新间隔（秒）", "refresh_seconds"),
            ("低余额提醒", "low_threshold"),
        ]
        for row, (label, key) in enumerate(labels):
            ttk.Label(frame, text=label).grid(row=row, column=0, sticky="w", pady=4, padx=(0, 12))
            if key == "auth_mode":
                widget = ttk.Combobox(frame, textvariable=fields[key], values=("bearer", "authorization", "x-api-key", "websee-session"), state="readonly", width=27)
            else:
                widget = ttk.Entry(frame, textvariable=fields[key], width=30, show="*" if key == "token" else "")
            widget.grid(row=row, column=1, sticky="ew", pady=4)
        ttk.Label(frame, text="示例路径：data.balance 或 balance", foreground="#64748b").grid(row=7, column=0, columnspan=2, sticky="w", pady=(2, 10))

        def save_and_close() -> None:
            try:
                new_settings = Settings(
                    endpoint=fields["endpoint"].get().strip(),
                    auth_mode=fields["auth_mode"].get(),
                    token_blob=self.settings.token_blob,
                    balance_path=fields["balance_path"].get().strip(),
                    currency=fields["currency"].get().strip() or "USD",
                    refresh_seconds=max(30, int(fields["refresh_seconds"].get())),
                    low_threshold=float(fields["low_threshold"].get()),
                )
                token = fields["token"].get()
                if token:
                    new_settings.token_blob = protect_token(token)
                CONFIG_PATH.write_text(json.dumps(asdict(new_settings), ensure_ascii=False, indent=2), encoding="utf-8")
                self.settings = new_settings
                self.status = "配置已保存"
                dialog.destroy()
                self.draw_pet()
                self.refresh()
            except Exception as exc:
                messagebox.showerror("配置无效", str(exc), parent=dialog)

        buttons = ttk.Frame(frame)
        buttons.grid(row=8, column=0, columnspan=2, sticky="e", pady=(8, 0))
        ttk.Button(buttons, text="保存并刷新", command=save_and_close).pack(side="right")
        ttk.Button(buttons, text="取消", command=dialog.destroy).pack(side="right", padx=(0, 8))

    def run(self) -> None:
        self.root.mainloop()


if __name__ == "__main__":
    PetApp().run()
