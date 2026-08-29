from __future__ import annotations

import math
import random
import os
import ctypes
import sys
import threading
import time
from ctypes import wintypes
from pathlib import Path

# Register bundled native libraries before importing PySide6. This keeps Qt
# DLL resolution reliable when launched from a BAT file or a shortcut.
if sys.platform == "win32" and getattr(sys, "frozen", False):
    _bundle_root = Path(getattr(sys, "_MEIPASS", Path(sys.executable).resolve().parent))
    _dll_directories = (_bundle_root / "PySide6", _bundle_root / "shiboken6", _bundle_root)
    for _dll_directory in _dll_directories:
        if _dll_directory.is_dir():
            try:
                os.add_dll_directory(str(_dll_directory))
            except (AttributeError, OSError):
                pass
    os.environ["PATH"] = os.pathsep.join(
        str(path) for path in _dll_directories if path.is_dir()
    ) + os.pathsep + os.environ.get("PATH", "")

from PySide6.QtCore import QEasingCurve, QPoint, QRectF, QPropertyAnimation, Qt, QTimer, Signal, QObject, QUrl
from PySide6.QtGui import QColor, QFont, QIcon, QImage, QPainter, QPainterPath, QPen, QPixmap
from PySide6.QtMultimedia import QAudioOutput, QMediaPlayer
from PySide6.QtWidgets import (
    QApplication,
    QCheckBox,
    QComboBox,
    QDialog,
    QDoubleSpinBox,
    QFormLayout,
    QFrame,
    QHBoxLayout,
    QLabel,
    QLineEdit,
    QMenu,
    QPushButton,
    QSpinBox,
    QSystemTrayIcon,
    QVBoxLayout,
    QWidget,
)

import balance_pet as core


APP_DIR = Path(sys.executable).resolve().parent if getattr(sys, "frozen", False) else Path(__file__).resolve().parent
RESOURCE_DIR = Path(getattr(sys, "_MEIPASS", APP_DIR))
PET_PATH = RESOURCE_DIR / "assets" / "pet.png"
CHATGPT_PET_PATH = RESOURCE_DIR / "assets" / "chatgpt-dragon.png"
CHATGPT_CACHE_MARKER = RESOURCE_DIR / "assets" / "chatgpt-dragon.key-v2"
CHATGPT_SOURCE_CANDIDATES = (
    RESOURCE_DIR / "assets" / "chatgpt-dragon-source.png",
)
PRESS_SOUND_PATH = RESOURCE_DIR / "assets" / "press.mp3"
RELEASE_SOUND_PATH = RESOURCE_DIR / "assets" / "release.mp3"
INK = QColor("#263d78")
SOFT_INK = QColor("#667db1")
PAPER = QColor(255, 255, 255, 250)
CODEX_PROCESS_NAMES = {"CHATGPT.EXE", "CODEX.EXE"}
PET_STATES = {
    "idle", "loading", "success", "low", "error", "clicked",
    "codex-working", "codex-done", "inactive",
}
TH32CS_SNAPPROCESS = 0x00000002
INVALID_HANDLE_VALUE = ctypes.c_void_p(-1).value


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


def codex_is_running() -> bool:
    if sys.platform != "win32":
        return True
    snapshot = ctypes.windll.kernel32.CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0)
    if snapshot == INVALID_HANDLE_VALUE:
        return False
    entry = PROCESSENTRY32W()
    entry.dwSize = ctypes.sizeof(entry)
    try:
        ok = ctypes.windll.kernel32.Process32FirstW(snapshot, ctypes.byref(entry))
        while ok:
            if entry.szExeFile.upper() in CODEX_PROCESS_NAMES:
                return True
            ok = ctypes.windll.kernel32.Process32NextW(snapshot, ctypes.byref(entry))
    finally:
        ctypes.windll.kernel32.CloseHandle(snapshot)
    return False


class Events(QObject):
    fetched = Signal(float)
    failed = Signal(str)


class BalancePet(QWidget):
    def __init__(self, follow_codex: bool = False) -> None:
        super().__init__()
        self.follow_codex = follow_codex
        self.codex_was_running = codex_is_running() if follow_codex else True
        self.settings = core.load_settings()
        self.events = Events()
        self.events.fetched.connect(self.on_balance)
        self.events.failed.connect(self.on_error)
        self.pet = self.load_pet_pixmap(self.settings.pet_style)
        self.balance: float | None = None
        self.display_balance: float | None = None
        self.today_usage = 0.0
        self.pet_scale = max(0.75, min(1.25, float(getattr(self.settings, "pet_scale", 1.0))))
        self.render_pet = QPixmap()
        self.status = "idle"
        self.pet_state = "idle"
        self.state_before_click = "idle"
        self.state_timer = QTimer(self)
        self.state_timer.setSingleShot(True)
        self.state_timer.timeout.connect(self.restore_temporary_state)
        self.inactive_timer = QTimer(self)
        self.inactive_timer.setSingleShot(True)
        self.inactive_timer.timeout.connect(lambda: self.set_pet_state("inactive"))
        self.codex_task_seen = False
        self.error_detail = ""
        self.bubble_visible = False
        self.bubble_override: tuple[str, str, str] | None = None
        self.bubble_line: tuple[str, str, str] | None = None
        self.flipped = bool(getattr(self.settings, "flipped", False))
        self.drag_origin: QPoint | None = None
        self.window_origin: QPoint | None = None
        self.dragging = False
        self.pressed = False
        self.interaction_mode = str(getattr(self.settings, "interaction_mode", "free"))
        if self.interaction_mode not in ("free", "locked"):
            self.interaction_mode = "free"
        self.interaction_kind: str | None = None
        self.interaction_value = QPoint(0, 0)
        self.interaction_origin: QPoint | None = None
        self.menu_hover = False
        self.fetching = False
        self.show_after_fetch = False
        self.start_value = 0.0
        self.target_value = 0.0
        self.amount_started = 0.0
        self.float_phase = 0.0
        self.float_last_tick = time.perf_counter()

        self.setFixedSize(438, 430)
        self.setWindowFlags(Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint | Qt.Tool)
        self.setAttribute(Qt.WA_TranslucentBackground)
        self.setMouseTracking(True)
        self.restore_position()

        self.bubble_animation = QPropertyAnimation(self, b"windowOpacity")
        self.bubble_animation.setDuration(1)
        self.bubble_timer = QTimer(self)
        self.bubble_timer.setSingleShot(True)
        self.bubble_timer.timeout.connect(self.hide_bubble)
        self.press_player, self.press_audio = self.make_sound(PRESS_SOUND_PATH)
        self.release_player, self.release_audio = self.make_sound(RELEASE_SOUND_PATH)
        self.refresh_timer = QTimer(self)
        self.refresh_timer.timeout.connect(lambda: self.refresh(False))
        self.codex_monitor_timer = QTimer(self)
        self.codex_monitor_timer.timeout.connect(self.sync_codex_visibility)
        if self.follow_codex:
            self.codex_monitor_timer.start(2000)
        self.refresh_render_pet()
        self.setup_tray()
        self.frame_timer = QTimer(self)
        self.frame_timer.timeout.connect(self.animate)
        self.frame_timer.setTimerType(Qt.PreciseTimer)
        self.frame_timer.start(16)
        self.reset_inactive_timer()
        QTimer.singleShot(350, lambda: self.open_settings() if not self.settings.endpoint else self.refresh(False))

    def set_pet_state(self, state: str, *, temporary_ms: int = 0) -> None:
        """Central state transition point for visuals, bubbles, and future sprites."""
        if state not in PET_STATES:
            state = "idle"
        self.pet_state = state
        self.status = {
            "idle": "idle",
            "loading": "loading",
            "success": "ok",
            "low": "low",
            "error": "error",
            "clicked": "ok",
            "codex-working": "loading",
            "codex-done": "ok",
            "inactive": "idle",
        }[state]
        if temporary_ms > 0:
            self.state_timer.start(temporary_ms)
        self.refresh_render_pet()
        self.update()

    def restore_temporary_state(self) -> None:
        if self.balance is None:
            self.set_pet_state("idle")
        elif self.error_detail:
            self.set_pet_state("error")
        elif self.balance <= self.settings.low_threshold:
            self.set_pet_state("low")
        else:
            self.set_pet_state("success")

    def reset_inactive_timer(self) -> None:
        self.inactive_timer.start(15 * 60 * 1000)
        if self.pet_state == "inactive":
            self.restore_temporary_state()

    @staticmethod
    def load_pet_pixmap(style: str) -> QPixmap:
        if style != "chatgpt":
            return QPixmap(str(PET_PATH))
        source = next((path for path in CHATGPT_SOURCE_CANDIDATES if path.exists()), None)
        if CHATGPT_PET_PATH.exists() and (
            source is None
            or (CHATGPT_CACHE_MARKER.exists() and CHATGPT_PET_PATH.stat().st_mtime >= source.stat().st_mtime)
        ):
            return QPixmap(str(CHATGPT_PET_PATH))
        if source is None:
            return QPixmap(str(PET_PATH))
        image = QImage(str(source)).convertToFormat(QImage.Format_ARGB32)
        # Remove the generated magenta key with a feathered alpha edge. A hard
        # transparent/opaque cutoff leaves pink halos and jagged diagonal edges.
        for y in range(image.height()):
            for x in range(image.width()):
                pixel = image.pixelColor(x, y)
                red, green, blue = pixel.red(), pixel.green(), pixel.blue()
                magenta = max(0, min(red, blue) - green)
                if magenta > 22:
                    alpha = max(0, min(255, int((132 - magenta) * 255 / 110)))
                    # Suppress the pink spill on pixels retained at the edge.
                    spill = min(0.72, max(0.0, (magenta - 22) / 150))
                    red = int(red * (1.0 - spill))
                    blue = int(blue * (1.0 - spill))
                    pixel.setRgb(red, green, blue, alpha)
                    image.setPixelColor(x, y, pixel)
        bounds = image.rect()
        left, top, right, bottom = image.width(), image.height(), -1, -1
        for y in range(image.height()):
            for x in range(image.width()):
                if image.pixelColor(x, y).alpha() > 0:
                    left, top = min(left, x), min(top, y)
                    right, bottom = max(right, x), max(bottom, y)
        if right >= left and bottom >= top:
            bounds = QRectF(left - 20, top - 20, right - left + 41, bottom - top + 41).toRect().intersected(image.rect())
        cropped = image.copy(bounds)
        cropped.save(str(CHATGPT_PET_PATH), "PNG")
        CHATGPT_CACHE_MARKER.write_text("chroma-key-v2\n", encoding="ascii")
        return QPixmap.fromImage(cropped)

    def reload_pet(self) -> None:
        self.pet = self.load_pet_pixmap(self.settings.pet_style)
        if hasattr(self, "tray_icon"):
            self.tray_icon.setIcon(QIcon(self.pet))
        self.refresh_render_pet()
        self.update()

    def load_state_pixmap(self, state: str) -> QPixmap:
        """Load an optional state pose, falling back to the approved base art.

        This keeps the current single-image pet working while making the runtime
        ready for state-specific PNG frames or exported animation poses.
        """
        style = str(getattr(self.settings, "pet_style", "deepseek"))
        candidates = (
            RESOURCE_DIR / "assets" / "pets" / style / f"{state}.png",
            RESOURCE_DIR / "assets" / "pets" / f"{style}-{state}.png",
        )
        for candidate in candidates:
            if candidate.exists():
                pixmap = QPixmap(str(candidate))
                if not pixmap.isNull():
                    return pixmap
        return self.pet

    def restore_position(self) -> None:
        saved_x = int(getattr(self.settings, "window_x", -1))
        saved_y = int(getattr(self.settings, "window_y", -1))
        if saved_x < 0 and saved_y < 0:
            return
        candidate = QPoint(saved_x + self.width() // 2, saved_y + self.height() // 2)
        screen = QApplication.screenAt(candidate) or QApplication.primaryScreen()
        if screen is None:
            return
        area = screen.availableGeometry()
        x = max(area.left(), min(saved_x, area.right() - self.width() + 1))
        y = max(area.top(), min(saved_y, area.bottom() - self.height() + 1))
        self.move(x, y)

    def save_layout(self) -> None:
        self.settings.pet_scale = float(self.pet_scale)
        self.settings.window_x = int(self.x())
        self.settings.window_y = int(self.y())
        self.settings.flipped = bool(self.flipped)
        try:
            core.save_settings(self.settings, core.unprotect_token(self.settings.token_blob))
        except Exception:
            pass

    def setup_tray(self) -> None:
        self.tray_icon = QSystemTrayIcon(QIcon(self.pet), self)
        self.tray_icon.setToolTip("余额桌宠")
        tray_menu = QMenu(self)
        tray_menu.addAction("显示桌宠", self.show_pet)
        tray_menu.addAction("立即刷新", lambda: self.refresh(True))
        tray_menu.addAction("配置接口", self.open_settings)
        tray_menu.addSeparator()
        tray_menu.addAction("退出", QApplication.quit)
        self.tray_icon.setContextMenu(tray_menu)
        self.tray_icon.activated.connect(self.on_tray_activated)
        self.tray_icon.show()

    def on_tray_activated(self, reason: QSystemTrayIcon.ActivationReason) -> None:
        if reason in (QSystemTrayIcon.Trigger, QSystemTrayIcon.DoubleClick):
            self.show_pet()

    def show_pet(self) -> None:
        self.show()
        self.raise_()
        self.activateWindow()

    def sync_codex_visibility(self) -> None:
        if not self.follow_codex:
            return
        running = codex_is_running()
        if running and not self.codex_was_running:
            self.show_pet()
            self.codex_task_seen = True
            self.set_pet_state("codex-working")
            self.show_bubble()
        elif not running and self.codex_was_running:
            if self.fetching or self.codex_task_seen:
                self.set_pet_state("codex-done", temporary_ms=6200)
                self.bubble_override = ("Codex 完成", "任务结束", "余额变化将在刷新后显示")
                self.show_bubble()
            self.save_layout()
            self.hide()
        self.codex_was_running = running

    def refresh_render_pet(self) -> None:
        if self.pet.isNull() or self.pet_scale <= 0:
            self.render_pet = QPixmap()
            return
        # Keep the original pixels. draw_pet() scales the source once at paint time,
        # which keeps facial details sharper than pre-scaling it to the pet window.
        self.render_pet = self.load_state_pixmap(self.pet_state)

    def make_sound(self, path: Path) -> tuple[QMediaPlayer, QAudioOutput]:
        audio = QAudioOutput(self)
        audio.setVolume(0.32)
        player = QMediaPlayer(self)
        player.setAudioOutput(audio)
        player.setSource(QUrl.fromLocalFile(str(path)))
        return player, audio

    @staticmethod
    def play_sound(player: QMediaPlayer) -> None:
        player.stop()
        player.setPosition(0)
        player.play()

    def paintEvent(self, _event) -> None:  # Qt owns the transparency and all visual animation.
        painter = QPainter(self)
        painter.setRenderHint(QPainter.Antialiasing)
        self.draw_bubble(painter)
        self.draw_pet(painter)

    def draw_bubble(self, painter: QPainter) -> None:
        if not self.bubble_visible or not getattr(self.settings, "bubble", True):
            return
        painter.save()
        painter.setPen(QPen(INK, 5))
        painter.setBrush(PAPER)
        painter.drawEllipse(QRectF(9, 13, 300, 207))
        painter.drawEllipse(QRectF(246, 215, 36, 26))
        painter.drawEllipse(QRectF(286, 257, 22, 16))
        painter.setPen(Qt.NoPen)
        if self.bubble_override is not None:
            label, amount, hint = self.bubble_override
        elif self.bubble_line is not None:
            label, amount, hint = self.bubble_line
        else:
            label, amount, hint = self.normal_lines()
        center = QRectF(25, 62, 265, 115)
        painter.setPen(SOFT_INK)
        painter.setFont(QFont("Microsoft YaHei UI", 14, QFont.DemiBold))
        painter.drawText(QRectF(center.left(), 5 + center.top(), center.width(), 30), Qt.AlignCenter, label)
        painter.setPen(INK)
        painter.setFont(QFont("Segoe UI", 29, QFont.Bold))
        painter.drawText(QRectF(center.left(), 40 + center.top(), center.width(), 48), Qt.AlignCenter, amount)
        painter.setPen(QColor("#94a4c8"))
        painter.setFont(QFont("Microsoft YaHei UI", 11))
        painter.drawText(QRectF(center.left(), 91 + center.top(), center.width(), 30), Qt.AlignCenter, hint[:32])
        painter.restore()

    def draw_pet(self, painter: QPainter) -> None:
        # Keep the sub-pixel offset; rounding it to int makes the pet visibly jump.
        float_amplitude = {
            "idle": 3.5,
            "success": 3.8,
            "low": 2.7,
            "loading": 3.0,
            "codex-working": 3.2,
            "codex-done": 4.3,
            "clicked": 0.8,
            "error": 1.8,
            "inactive": 1.4,
        }.get(self.pet_state, 3.5)
        float_y = math.sin(self.float_phase) * float_amplitude if not self.pressed else 0.0
        anchored = self.pet_rect()
        target = QRectF(anchored.x(), anchored.y() + float_y, anchored.width(), anchored.height())
        x, y, size = target.x(), target.y(), target.width()
        painter.save()
        # These small transforms are deliberately procedural: they work with the
        # existing PNGs while leaving a clean seam for future sprite/rig assets.
        tilt = 0.0
        pulse = 1.0
        if self.pet_state in ("loading", "codex-working"):
            tilt = math.sin(self.float_phase * 2.0) * 1.2
            pulse = 1.0 + math.sin(self.float_phase * 2.0) * 0.008
        elif self.pet_state == "clicked":
            pulse = 1.0 + math.sin(self.float_phase * 5.0) * 0.018
        elif self.pet_state == "codex-done":
            tilt = math.sin(self.float_phase * 2.4) * 1.8
        elif self.pet_state == "inactive":
            painter.setOpacity(0.78)
        interaction_tilt = 0.0
        if self.interaction_kind == "hair":
            interaction_tilt = max(-8.0, min(8.0, self.interaction_value.x() * 0.16))
            target = QRectF(target.x(), target.y() + max(-9.0, min(9.0, self.interaction_value.y() * 0.08)), target.width(), target.height())
        elif self.interaction_kind == "mouth":
            pull = max(-0.035, min(0.035, self.interaction_value.x() / 2600.0))
            pulse *= 1.0 + pull
            interaction_tilt = max(-3.0, min(3.0, self.interaction_value.x() * 0.06))
        tilt += interaction_tilt
        if tilt or pulse != 1.0:
            painter.translate(target.center())
            painter.rotate(tilt)
            painter.scale(pulse, pulse)
            painter.translate(-target.center())
        if self.pressed:
            painter.translate(target.center().x(), target.bottom())
            painter.scale(1.06, 0.88)
            painter.translate(-target.center().x(), -target.bottom())
        if self.flipped:
            painter.translate(target.center().x() * 2, 0)
            painter.scale(-1, 1)
        painter.setRenderHint(QPainter.SmoothPixmapTransform, True)
        scaled = self.render_pet
        scale = min(target.width() / scaled.width(), target.height() / scaled.height())
        image_width = scaled.width() * scale
        image_height = scaled.height() * scale
        image_rect = QRectF(
            target.x() + (target.width() - image_width) / 2,
            target.y() + (target.height() - image_height) / 2,
            image_width,
            image_height,
        )
        painter.drawPixmap(image_rect, scaled, QRectF(scaled.rect()))
        painter.restore()

        dot = QColor("#91a2c8")
        if self.pet_state in ("loading", "codex-working"): dot = QColor("#f0a93d")
        elif self.pet_state in ("success", "clicked", "codex-done"): dot = QColor("#35b77d")
        elif self.pet_state in ("low", "error"): dot = QColor("#ef5b71")
        dot_x = x + 28 if self.flipped else x + size - 28
        dot_y = y + size - 35
        painter.setPen(QPen(QColor("white"), 3))
        painter.setBrush(dot)
        painter.drawEllipse(QRectF(dot_x - 8, dot_y - 8, 16, 16))

        menu_rect = self.menu_rect()
        alpha = 220 if self.menu_hover else 112
        painter.setPen(Qt.NoPen)
        painter.setBrush(QColor(255, 255, 255, alpha))
        painter.drawEllipse(menu_rect)
        center_y = menu_rect.center().y()
        for offset in (-5, 0, 5):
            painter.setBrush(QColor(82, 106, 164, alpha))
            painter.drawEllipse(QRectF(menu_rect.center().x() + offset - 1.45, center_y - 1.45, 2.9, 2.9))

    def normal_lines(self) -> tuple[str, str, str]:
        if self.pet_state == "loading":
            return "正在刷新", self.format_amount(self.display_balance), "正在联系中转站"
        if self.pet_state == "codex-working":
            return "Codex 工作中", self.format_amount(self.display_balance), "我在旁边看着余额"
        if self.pet_state == "codex-done":
            return "Codex 完成", self.format_amount(self.display_balance), "准备显示本次变化"
        if self.pet_state == "error":
            detail = self.error_detail or "请检查接口或令牌"
            return "刷新失败", self.format_amount(self.display_balance), detail[:32]
        if self.balance is None:
            return "账户余额", "--", "点击小鲸鱼刷新"
        if self.pet_state == "clicked":
            return "收到点击", self.format_amount(self.display_balance), "再点一次可以刷新余额"
        label = "余额偏低" if self.pet_state == "low" else "账户余额"
        return label, self.format_amount(self.display_balance), f"今日已用 {self.format_amount(self.today_usage)}"

    def format_amount(self, value: float | None) -> str:
        if value is None:
            return "--"
        currency = self.settings.currency.upper()
        if currency in ("CNY", "RMB"):
            return f"¥{value:,.2f}"
        if currency == "USD":
            return f"${value:,.2f}"
        return f"{value:,.2f} {currency}"

    def animate(self) -> None:
        now = time.perf_counter()
        elapsed = min(0.05, max(0.0, now - self.float_last_tick))
        self.float_last_tick = now
        # Time-based phase keeps the speed stable even if Windows delays a timer tick.
        speed = {
            "idle": 1.6,
            "success": 1.8,
            "low": 1.35,
            "loading": 2.15,
            "codex-working": 2.0,
            "codex-done": 2.25,
            "clicked": 2.8,
            "error": 1.1,
            "inactive": 0.65,
        }.get(self.pet_state, 1.6)
        self.float_phase = (self.float_phase + elapsed * speed) % math.tau
        if self.display_balance is not None and self.amount_started:
            progress = min(1.0, (time.perf_counter() - self.amount_started) / 0.72)
            eased = 1 - (1 - progress) ** 3
            self.display_balance = self.start_value + (self.target_value - self.start_value) * eased
            if progress >= 1:
                self.amount_started = 0.0
        if self.interaction_kind is not None and not self.pressed:
            self.interaction_value = QPoint(
                round(self.interaction_value.x() * 0.82),
                round(self.interaction_value.y() * 0.82),
            )
            if self.interaction_value.manhattanLength() < 2:
                self.interaction_kind = None
        self.update()

    def pet_rect(self) -> QRectF:
        scale = self.pet_scale
        size = 252 * scale
        margin = 0
        x = margin if self.flipped else self.width() - size - margin
        y = self.height() - size - margin
        return QRectF(x, y, size, size)

    def menu_rect(self) -> QRectF:
        pet = self.pet_rect()
        diameter = 22
        x = pet.left() + 10 if self.flipped else pet.right() - diameter - 10
        return QRectF(x, pet.top() + 10, diameter, diameter)

    def menu_hit_rect(self) -> QRectF:
        return self.menu_rect().adjusted(-8, -8, 8, 8)

    def mousePressEvent(self, event) -> None:
        if event.button() != Qt.LeftButton:
            return
        if self.menu_hit_rect().contains(event.position()):
            self.open_settings()
            return
        if self.bubble_visible and QRectF(9, 13, 300, 207).contains(event.position()):
            self.bubble_override = None
            self.bubble_line = random.choice([
                ("状态良好", "余额充足", "今天也可以安心工作"),
                ("我看着呢", "不会漏掉", "余额变化会及时告诉你"),
                ("省着点花", "细水长流", "低余额时我会变红"),
                ("工作时间", "陪你写完", "然后记得休息一下"),
            ])
            self.bubble_timer.start(6200)
            self.update()
            return
        if self.pet_rect().contains(event.position()):
            self.reset_inactive_timer()
            self.state_before_click = self.pet_state
            self.set_pet_state("clicked", temporary_ms=900)
            self.drag_origin = event.globalPosition().toPoint()
            self.interaction_origin = self.drag_origin
            self.interaction_value = QPoint(0, 0)
            self.window_origin = self.pos() if self.interaction_mode == "free" else None
            self.interaction_kind = self.hit_interaction(event.position()) if self.interaction_mode == "locked" else None
            self.dragging = False
            self.pressed = True
            self.play_sound(self.press_player)
            self.update()

    def mouseMoveEvent(self, event) -> None:
        self.reset_inactive_timer()
        self.menu_hover = self.pet_rect().contains(event.position())
        if self.interaction_mode == "locked" and self.interaction_origin is not None and self.pressed:
            delta = event.globalPosition().toPoint() - self.interaction_origin
            if self.interaction_kind == "hair":
                self.interaction_value = QPoint(max(-45, min(45, delta.x())), max(-70, min(25, delta.y())))
            elif self.interaction_kind == "mouth":
                self.interaction_value = QPoint(max(-55, min(55, delta.x())), max(-12, min(12, delta.y())))
            self.update()
            return
        if self.drag_origin is None or self.window_origin is None:
            self.update()
            return
        delta = event.globalPosition().toPoint() - self.drag_origin
        if delta.manhattanLength() > 3:
            self.dragging = True
            self.move(self.window_origin + delta)

    def mouseReleaseEvent(self, event) -> None:
        if event.button() != Qt.LeftButton or self.drag_origin is None:
            return
        was_dragged = self.dragging
        interaction_kind = self.interaction_kind
        self.pressed = False
        self.drag_origin = self.window_origin = None
        self.interaction_origin = None
        if self.interaction_mode == "locked":
            if interaction_kind == "hair":
                self.bubble_override = ("呆毛被提起", "哎呀", "轻一点嘛")
                self.show_bubble()
            elif interaction_kind == "mouth":
                self.bubble_override = ("嘴角被拽住", "诶？", "还要继续吗")
                self.show_bubble()
            else:
                self.bubble_line = None
                self.show_bubble()
                self.play_sound(self.release_player)
                self.refresh(True)
            self.update()
            return
        if was_dragged:
            self.snap()
        else:
            self.bubble_line = None
            self.show_bubble()
            self.play_sound(self.release_player)
            self.refresh(True)
        self.update()

    def leaveEvent(self, _event) -> None:
        self.menu_hover = False
        self.update()

    def contextMenuEvent(self, event) -> None:
        menu = QMenu(self)
        menu.addAction("立即刷新", lambda: self.refresh(True))
        menu.addAction("配置接口", self.open_settings)
        menu.addAction("切换为锁定互动" if self.interaction_mode == "free" else "切换为自由拖动", self.toggle_interaction_mode)
        menu.addSeparator()
        menu.addAction("隐藏", self.hide)
        menu.addAction("退出", QApplication.quit)
        menu.exec(event.globalPos())

    def toggle_interaction_mode(self) -> None:
        self.interaction_mode = "locked" if self.interaction_mode == "free" else "free"
        self.settings.interaction_mode = self.interaction_mode
        self.save_layout()
        self.bubble_override = (
            "已锁定互动" if self.interaction_mode == "locked" else "已开启拖动",
            "模式已切换",
            "头顶和脸部可进行互动" if self.interaction_mode == "locked" else "按住宠物即可移动",
        )
        self.show_bubble()

    def hit_interaction(self, position: QPoint | object) -> str:
        """Choose a hotspot until layered or rigged art is available."""
        point = position if isinstance(position, QPoint) else position.toPoint()
        pet = self.pet_rect()
        if not pet.contains(point):
            return "body"
        rel_x = (point.x() - pet.left()) / max(1.0, pet.width())
        rel_y = (point.y() - pet.top()) / max(1.0, pet.height())
        if rel_y < 0.28:
            return "hair"
        if 0.24 < rel_y < 0.72 and 0.18 < rel_x < 0.82:
            return "mouth"
        return "body"

    def snap(self) -> None:
        screen = QApplication.screenAt(self.frameGeometry().center()) or QApplication.primaryScreen()
        area = screen.availableGeometry()
        x, y = self.x(), self.y()
        center_x, center_y = x + self.width() / 2, y + self.height() / 2
        target_x, target_y = x, y
        if center_x < area.left() + area.width() / 4:
            target_x = area.left()
        elif center_x > area.left() + area.width() * .75:
            target_x = area.right() - self.width() + 1
        if center_y < area.top() + area.height() / 4:
            target_y = area.top()
        elif center_y > area.top() + area.height() * .75:
            target_y = area.bottom() - self.height() + 1
        self.flipped = target_x == area.left()
        animation = QPropertyAnimation(self, b"pos", self)
        animation.setDuration(170)
        animation.setStartValue(QPoint(x, y))
        animation.setEndValue(QPoint(target_x, target_y))
        animation.setEasingCurve(QEasingCurve.OutCubic)
        animation.finished.connect(animation.deleteLater)
        animation.finished.connect(self.save_layout)
        animation.start()

    def show_bubble(self) -> None:
        if not getattr(self.settings, "bubble", True):
            return
        self.bubble_visible = True
        self.bubble_line = None
        self.bubble_timer.start(6200)
        self.update()

    def hide_bubble(self) -> None:
        self.bubble_timer.stop()
        self.bubble_visible = False
        self.bubble_override = None
        self.bubble_line = None
        self.update()

    def refresh(self, manual: bool) -> None:
        if self.fetching:
            return
        self.fetching = True
        self.show_after_fetch = manual
        self.set_pet_state("loading")
        if manual:
            self.show_bubble()
        def work() -> None:
            try:
                self.events.fetched.emit(core.fetch_balance(self.settings))
            except Exception as error:
                self.events.failed.emit(str(error))
        threading.Thread(target=work, daemon=True).start()

    def on_balance(self, amount: float) -> None:
        previous_balance = self.balance
        self.balance = amount
        if self.display_balance is None:
            self.display_balance = amount
            self.amount_started = 0.0
        else:
            self.start_value, self.target_value = self.display_balance, amount
            self.amount_started = time.perf_counter()
        self.today_usage = self.observe_usage(amount)
        self.set_pet_state("low" if amount <= self.settings.low_threshold else "success")
        self.error_detail = ""
        self.fetching = False
        self.reset_inactive_timer()
        if previous_balance is not None and amount < previous_balance:
            spent = previous_balance - amount
            self.bubble_override = (
                "本次消耗",
                f"-{self.format_amount(spent)}",
                f"当前余额 {self.format_amount(amount)}",
            )
            self.show_bubble()
        elif previous_balance is None or self.show_after_fetch:
            self.show_bubble()
        self.show_after_fetch = False
        self.refresh_timer.start(max(30, int(self.settings.refresh_seconds)) * 1000)

    def on_error(self, message: str) -> None:
        self.set_pet_state("error")
        self.error_detail = message
        self.fetching = False
        self.reset_inactive_timer()
        self.show_bubble()
        self.refresh_timer.start(max(30, int(self.settings.refresh_seconds)) * 1000)

    def observe_usage(self, amount: float) -> float:
        # Reuse the prior balance as a lightweight, no-token daily-use indicator.
        ledger_path = APP_DIR / "balance-pet-usage.json"
        today = time.strftime("%Y-%m-%d")
        try:
            ledger = __import__("json").loads(ledger_path.read_text(encoding="utf-8"))
        except Exception:
            ledger = {"date": today, "balance": amount, "usage": 0.0}
        if ledger.get("date") != today:
            ledger = {"date": today, "balance": amount, "usage": 0.0}
        elif ledger.get("currency") == self.settings.currency and amount < float(ledger.get("balance", amount)):
            ledger["usage"] = float(ledger.get("usage", 0)) + float(ledger["balance"]) - amount
        ledger.update(balance=amount, currency=self.settings.currency)
        try:
            ledger_path.write_text(__import__("json").dumps(ledger, ensure_ascii=False, indent=2), encoding="utf-8")
        except OSError:
            pass
        return float(ledger.get("usage", 0.0))

    def open_settings(self) -> None:
        dialog = SettingsDialog(self, self.settings)
        if dialog.exec() == QDialog.Accepted:
            self.settings = dialog.settings
            self.pet_scale = dialog.pet_scale
            self.interaction_mode = dialog.settings.interaction_mode
            self.save_layout()
            self.reload_pet()
            self.update()
            self.refresh(True)


class SettingsDialog(QDialog):
    def __init__(self, parent: QWidget, settings: core.Settings) -> None:
        super().__init__(parent)
        self.settings = settings
        self.setWindowTitle("小余额设置")
        self.setMinimumWidth(430)
        self.setStyleSheet("""
          QDialog { background: #f7f9fd; } QLabel { color: #52658f; } QLineEdit, QComboBox, QSpinBox, QDoubleSpinBox { min-height: 28px; border: 1px solid #cad6ed; border-radius: 5px; padding: 2px 7px; background: white; color: #263d78; } QPushButton { min-height: 32px; border: 0; border-radius: 6px; padding: 0 13px; font-weight: 600; background: #263d78; color: white; } QPushButton#secondary { background: #e8eef9; color: #263d78; }
        """)
        layout = QVBoxLayout(self)
        title = QLabel("小余额")
        title.setStyleSheet("font-size: 21px; font-weight: 700; color: #263d78;")
        layout.addWidget(title)
        subtitle = QLabel("令牌将使用 Windows DPAPI 加密保存")
        subtitle.setStyleSheet("color: #8a9abd; margin-bottom: 10px;")
        layout.addWidget(subtitle)
        form = QFormLayout()
        self.endpoint = QLineEdit(settings.endpoint)
        self.auth = QComboBox()
        self.auth.addItem("Bearer Token（只填令牌）", "bearer")
        self.auth.addItem("完整 Authorization（需含 Bearer）", "authorization")
        self.auth.addItem("x-api-key", "x-api-key")
        self.auth.addItem("中转站会话（websee-session）", "websee-session")
        self.auth.setCurrentIndex(max(0, self.auth.findData(settings.auth_mode)))
        self.pet_style = QComboBox()
        self.pet_style.addItem("DeepSeek 小鲸鱼", "deepseek")
        self.pet_style.addItem("ChatGPT 小龙助手", "chatgpt")
        self.pet_style.setCurrentIndex(max(0, self.pet_style.findData(settings.pet_style)))
        self.interaction_mode = QComboBox()
        self.interaction_mode.addItem("自由拖动", "free")
        self.interaction_mode.addItem("锁定互动", "locked")
        self.interaction_mode.setCurrentIndex(max(0, self.interaction_mode.findData(getattr(settings, "interaction_mode", "free"))))
        self.token = QLineEdit(); self.token.setEchoMode(QLineEdit.Password); self.token.setPlaceholderText("留空保持现有令牌")
        self.path = QLineEdit(settings.balance_path)
        self.currency = QLineEdit(settings.currency)
        self.interval = QSpinBox(); self.interval.setRange(30, 86400); self.interval.setValue(settings.refresh_seconds)
        self.threshold = QDoubleSpinBox(); self.threshold.setRange(-1e9, 1e9); self.threshold.setDecimals(2); self.threshold.setValue(settings.low_threshold)
        self.scale = QDoubleSpinBox(); self.scale.setRange(.75, 1.25); self.scale.setSingleStep(.05); self.scale.setValue(float(getattr(settings, "pet_scale", 1.0)))
        self.pet_scale = float(getattr(settings, "pet_scale", 1.0))
        form.addRow("余额 API 地址", self.endpoint)
        form.addRow("认证方式", self.auth)
        form.addRow("宠物形象", self.pet_style)
        form.addRow("交互模式", self.interaction_mode)
        form.addRow("访问令牌", self.token)
        form.addRow("余额 JSON 路径", self.path)
        form.addRow("货币显示", self.currency)
        form.addRow("刷新秒数", self.interval)
        form.addRow("低余额阈值", self.threshold)
        form.addRow("宠物大小", self.scale)
        layout.addLayout(form)
        controls = QHBoxLayout()
        cancel = QPushButton("取消"); cancel.setObjectName("secondary"); cancel.clicked.connect(self.reject)
        save = QPushButton("保存并测试"); save.clicked.connect(self.save)
        controls.addStretch(); controls.addWidget(cancel); controls.addWidget(save)
        layout.addLayout(controls)

    def save(self) -> None:
        self.pet_scale = self.scale.value()
        self.settings = core.Settings(
            endpoint=self.endpoint.text().strip(),
            auth_mode=self.auth.currentData() or "bearer",
            token_blob=self.settings.token_blob,
            balance_path=self.path.text().strip(),
            currency=self.currency.text().strip().upper() or "USD",
            refresh_seconds=self.interval.value(),
            low_threshold=self.threshold.value(),
            pet_style=self.pet_style.currentData() or "deepseek",
            pet_scale=self.scale.value(),
            window_x=self.settings.window_x,
            window_y=self.settings.window_y,
            flipped=self.settings.flipped,
            interaction_mode=self.interaction_mode.currentData() or "free",
        )
        token = self.token.text().strip()
        if not token:
            token = core.unprotect_token(self.settings.token_blob)
        core.save_settings(self.settings, token)
        self.accept()


if __name__ == "__main__":
    app = QApplication([])
    app.setQuitOnLastWindowClosed(False)
    pet = BalancePet(follow_codex="--follow-codex" in sys.argv)
    app.aboutToQuit.connect(pet.save_layout)
    if not pet.follow_codex or pet.codex_was_running:
        pet.show()
    app.exec()
