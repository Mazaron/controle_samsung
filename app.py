"""Controle de mesa para TVs Samsung Tizen na rede local."""

from __future__ import annotations

import base64
import argparse
import time
import json
import os
import ssl
import sys
import threading
import tkinter as tk
from pathlib import Path
from tkinter import messagebox

import requests
import websocket
from dotenv import load_dotenv
from PIL import Image, ImageDraw
import pystray
from urllib3.exceptions import InsecureRequestWarning
from wakeonlan import wake


ROOT = Path(sys.executable).resolve().parent if getattr(sys, "frozen", False) else Path(__file__).resolve().parent
if (ROOT / ".env").exists():
    load_dotenv(ROOT / ".env")
elif (ROOT.parent / ".env").exists():
    load_dotenv(ROOT.parent / ".env")
IP_TV = os.getenv("IP", "").strip()
TOKEN = os.getenv("TOKEN_TV", "").strip()
MAC_TV = os.getenv("MAC_TV", "").strip()
IP_BROADCAST = os.getenv("IP_BROADCAST", "255.255.255.255").strip()
APP_NAME = "Controle Samsung TV"

STREAMING_APPS = {
    "Netflix": ("3201907018807", "11101200001"),
    "Prime Video": ("3201910019365", "3201512006785"),
    "Globoplay": ("3201908019022",),
    "YouTube": ("111299001912",),
    "Disney+": ("3201901017640",),
}

BG = "#0b1020"
PANEL = "#141d31"
PANEL_2 = "#1b2740"
TEXT = "#f4f7ff"
MUTED = "#94a3bd"
ACCENT = "#51e0c1"
BLUE = "#5b8cff"
RED = "#ff637d"
FONT = "Segoe UI"

requests.packages.urllib3.disable_warnings(category=InsecureRequestWarning)


def _websocket_url() -> str:
    if not IP_TV:
        raise RuntimeError("Configure o endereço IP no arquivo .env.")
    name = base64.b64encode(b"Controle-Samsung-Windows").decode()
    url = f"wss://{IP_TV}:8002/api/v2/channels/samsung.remote.control?name={name}"
    if TOKEN:
        url += f"&token={TOKEN}"
    return url


def get_tv_status() -> dict:
    if not IP_TV:
        raise RuntimeError("Configure o endereço IP no arquivo .env.")
    response = requests.get(f"https://{IP_TV}:8002/api/v2/", timeout=4, verify=False)
    response.raise_for_status()
    data = response.json()
    return data.get("device", {})


def send_key(key: str) -> None:
    ws = websocket.create_connection(
        _websocket_url(), timeout=7, sslopt={"cert_reqs": ssl.CERT_NONE}
    )
    try:
        while True:
            event = json.loads(ws.recv())
            name = event.get("event")
            if name == "ms.channel.connect":
                break
            if name == "ms.channel.unauthorized":
                raise RuntimeError("A TV recusou o controle. Aprove a conexão na tela da TV.")
        ws.send(json.dumps({
            "method": "ms.remote.control",
            "params": {
                "Cmd": "Click",
                "DataOfCmd": key,
                "Option": "false",
                "TypeOfRemote": "SendRemoteKey",
            },
        }))
    finally:
        ws.close()


def wake_tv() -> None:
    if not MAC_TV:
        raise RuntimeError("Configure MAC_TV no arquivo .env para usar Wake-on-LAN.")
    wake(MAC_TV, host=IP_BROADCAST)


def launch_app(app_name: str) -> None:
    if not IP_TV:
        raise RuntimeError("Configure o endereço IP no arquivo .env.")
    if get_tv_status().get("PowerState") != "on":
        wake_tv()
        time.sleep(12)
    errors = []
    for app_id in STREAMING_APPS[app_name]:
        try:
            response = requests.post(
                f"http://{IP_TV}:8001/api/v2/applications/{app_id}", timeout=7
            )
            response.raise_for_status()
            return
        except requests.RequestException as exc:
            errors.append(str(exc))
    raise RuntimeError(
        f"A TV não abriu {app_name}. Confirme que o app está instalado e tente novamente."
    )


def power_off_tv() -> None:
    status = get_tv_status()
    if status.get("PowerState") != "on":
        raise RuntimeError("A TV já não está ligada; nenhum comando foi enviado.")
    send_key("KEY_POWER")


def toggle_power() -> None:
    """Ligue por Wake-on-LAN ou desligue com o controle quando conectada."""
    try:
        status = get_tv_status()
    except requests.RequestException:
        # A TV em standby não responde ao endpoint; WOL continua disponível.
        wake_tv()
        return
    if status.get("PowerState") == "on":
        power_off_tv()
    else:
        wake_tv()


def make_tray_image() -> Image.Image:
    image = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.rounded_rectangle((5, 9, 59, 47), radius=10, fill=ACCENT)
    draw.rounded_rectangle((10, 14, 54, 42), radius=6, fill="#111b2c")
    draw.line((22, 54, 42, 54), fill=ACCENT, width=4)
    draw.line((32, 47, 32, 54), fill=ACCENT, width=4)
    draw.ellipse((29, 27, 35, 33), fill=ACCENT)
    return image


class TVApp:
    def __init__(self) -> None:
        self.root = tk.Tk()
        self.root.title(APP_NAME)
        self.root.geometry("398x920")
        self.root.minsize(380, 850)
        self.root.configure(bg=BG)
        self.root.protocol("WM_DELETE_WINDOW", self.hide_window)
        self.root.option_add("*Font", (FONT, 10))
        self.state_text = tk.StringVar(value="Verificando conexão…")
        self.model_text = tk.StringVar(value="Samsung TV")
        self.content_title = tk.StringVar(value="Sem mídia identificada")
        self.content_subtitle = tk.StringVar(value="Abra um app de streaming ou atualize o estado da TV.")
        self.status_dot = tk.StringVar(value="●")
        self.startup_var = tk.BooleanVar(value=self.startup_enabled())
        self._build_ui()
        self._build_tray()
        self.root.after(350, self.refresh_status)
        self.root.after(15000, self.refresh_loop)

    def _build_ui(self) -> None:
        self.root.geometry("450x830")
        self.root.minsize(430, 760)
        outer = tk.Frame(self.root, bg=BG)
        outer.pack(fill="both", expand=True, padx=15, pady=13)

        header = tk.Frame(outer, bg=BG)
        header.pack(fill="x", pady=(0, 13))
        mark = tk.Label(header, text="⌂", bg=BLUE, fg=TEXT, font=(FONT, 15, "bold"),
                        width=3, pady=3)
        mark.pack(side="left", padx=(0, 9))
        titles = tk.Frame(header, bg=BG)
        titles.pack(side="left", fill="x", expand=True)
        tk.Label(titles, text="Sala de estar", bg=BG, fg=TEXT,
                 font=(FONT, 16, "bold")).pack(anchor="w")
        tk.Label(titles, text="SAMSUNG SMART TV", bg=BG, fg=ACCENT,
                 font=(FONT, 8, "bold")).pack(anchor="w")
        self._button(header, "⚙", lambda: self.select_page("controle"),
                     kind="subtle", width=3).pack(side="right", padx=(3, 0))
        self._button(header, "—", self.hide_window, kind="subtle", width=3).pack(side="right", padx=3)
        self._button(header, "×", self.hide_window, kind="subtle", width=3).pack(side="right", padx=3)

        tabs = tk.Frame(outer, bg=BG)
        tabs.pack(fill="x", pady=(0, 13))
        self.tab_buttons = {}
        for key, label in (("tv", "TV"), ("apps", "Aplicativos"), ("controle", "Controle")):
            button = self._button(tabs, label, lambda name=key: self.select_page(name),
                                  kind="secondary")
            button.pack(side="left", fill="x", expand=True, padx=3)
            self.tab_buttons[key] = button

        self.page_host = tk.Frame(outer, bg=BG)
        self.page_host.pack(fill="both", expand=True)
        self.pages = {}
        self._build_tv_page()
        self._build_apps_page()
        self._build_control_page()
        self.select_page("tv")

        footer = tk.Frame(outer, bg=BG)
        footer.pack(fill="x", pady=(11, 0))
        tk.Checkbutton(footer, text="Iniciar com o Windows", variable=self.startup_var,
                       command=self.toggle_startup, bg=BG, fg=MUTED, activebackground=BG,
                       activeforeground=TEXT, selectcolor=PANEL, font=(FONT, 9),
                       bd=0, highlightthickness=0).pack(side="left")
        self.message = tk.StringVar(value="Fechar minimiza na bandeja do Windows.")
        tk.Label(outer, textvariable=self.message, bg=BG, fg=MUTED,
                 font=(FONT, 8), anchor="w").pack(fill="x", pady=(4, 0))

    def _page(self, name: str) -> tk.Frame:
        frame = tk.Frame(self.page_host, bg=BG)
        self.pages[name] = frame
        return frame

    def _build_tv_page(self) -> None:
        page = self._page("tv")
        card = self._card(page)
        card.pack(fill="both", expand=True)
        top = tk.Frame(card, bg=PANEL)
        top.pack(fill="x")
        tk.Label(top, textvariable=self.status_dot, bg=PANEL, fg=ACCENT,
                 font=(FONT, 13, "bold")).pack(side="left", padx=(0, 7))
        tk.Label(top, textvariable=self.state_text, bg=PANEL, fg=TEXT,
                 font=(FONT, 12, "bold")).pack(side="left")
        self._button(top, "⏻", lambda: self.run_action("ligar/desligar", toggle_power),
                     kind="danger", width=4).pack(side="right")
        tk.Label(card, textvariable=self.model_text, bg=PANEL, fg=MUTED,
                 font=(FONT, 9)).pack(anchor="w", pady=(3, 6))

        art = tk.Canvas(card, height=175, bg="#102035", highlightthickness=0)
        art.pack(fill="x", pady=(3, 12))
        art.create_oval(42, 20, 295, 215, fill="#18324a", outline="")
        art.create_rectangle(65, 37, 325, 159, fill="#51e0c1", outline="", width=0)
        art.create_rectangle(73, 45, 317, 151, fill="#142339", outline="")
        art.create_text(195, 97, text="SAMSUNG", fill="#d3e6f6", font=(FONT, 16, "bold"))
        art.create_line(160, 160, 150, 173, fill="#51e0c1", width=5)
        art.create_line(230, 160, 240, 173, fill="#51e0c1", width=5)
        art.create_line(143, 174, 247, 174, fill="#51e0c1", width=5)

        now = tk.Frame(card, bg=PANEL)
        now.pack(fill="x", pady=(0, 9))
        tk.Label(now, text="AGORA NA TV", bg=PANEL, fg=MUTED,
                 font=(FONT, 8, "bold")).pack(anchor="w")
        tk.Label(now, textvariable=self.content_title, bg=PANEL, fg=TEXT,
                 font=(FONT, 14, "bold")).pack(anchor="w", pady=(3, 1))
        tk.Label(now, textvariable=self.content_subtitle, bg=PANEL, fg=MUTED,
                 font=(FONT, 9), wraplength=350, justify="left").pack(anchor="w")

        volume = tk.Frame(card, bg=PANEL)
        volume.pack(fill="x", pady=(8, 12))
        self._button(volume, "−", lambda: self.press("KEY_VOLDOWN"), kind="subtle", width=4).pack(side="left")
        tk.Label(volume, text="VOLUME", bg=PANEL, fg=MUTED,
                 font=(FONT, 8, "bold")).pack(side="left", padx=12)
        tk.Frame(volume, bg="#29364d", height=7).pack(side="left", fill="x", expand=True, padx=5)
        self._button(volume, "+", lambda: self.press("KEY_VOLUP"), kind="subtle", width=4).pack(side="right")

        quick = tk.Frame(card, bg=PANEL)
        quick.pack(fill="x")
        for label, key, icon in (("Mudo", "KEY_MUTE", "◖"), ("Início", "KEY_HOME", "⌂"),
                                 ("Fonte", "KEY_SOURCE", "▣"), ("Guia", "KEY_GUIDE", "☷")):
            tile = self._button(quick, f"{icon}\n{label}", lambda k=key: self.press(k), kind="secondary")
            tile.pack(side="left", fill="x", expand=True, padx=3)

        app_row = tk.Frame(page, bg=BG)
        app_row.pack(fill="x", pady=(11, 0))
        for name, color in (("NETFLIX", "#e50914"), ("prime video", "#0879d1"), ("globoplay", "#ed6a2c")):
            button = self._button(app_row, name, lambda n=name.title(): self.open_streaming_app(n),
                                  kind="app")
            button.configure(bg=color, activebackground=color)
            button.pack(side="left", fill="x", expand=True, padx=3)

    def _build_apps_page(self) -> None:
        page = self._page("apps")
        card = self._card(page)
        card.pack(fill="both", expand=True)
        tk.Label(card, text="Seus aplicativos", bg=PANEL, fg=TEXT,
                 font=(FONT, 18, "bold")).pack(anchor="w")
        tk.Label(card, text="Toque para abrir direto na TV", bg=PANEL, fg=MUTED,
                 font=(FONT, 9)).pack(anchor="w", pady=(2, 12))
        styles = {"Netflix": "#e50914", "Prime Video": "#0879d1", "Globoplay": "#ed6a2c",
                  "YouTube": "#ff2020", "Disney+": "#173aa8"}
        for name, color in styles.items():
            row = tk.Frame(card, bg=PANEL_2, padx=12, pady=9)
            row.pack(fill="x", pady=4)
            badge = tk.Label(row, text=name[:1].upper(), bg=color, fg=TEXT,
                             font=(FONT, 13, "bold"), width=3, pady=5)
            badge.pack(side="left", padx=(0, 10))
            tk.Label(row, text=name, bg=PANEL_2, fg=TEXT,
                     font=(FONT, 11, "bold")).pack(side="left", fill="x", expand=True)
            self._button(row, "Abrir  ›", lambda n=name: self.open_streaming_app(n),
                         kind="subtle").pack(side="right")
        tk.Label(card, text="A TV precisa estar na mesma rede. Apps ausentes ou IDs diferentes serão informados.",
                 bg=PANEL, fg=MUTED, font=(FONT, 8), wraplength=340,
                 justify="left").pack(anchor="w", pady=(12, 0))

    def _build_control_page(self) -> None:
        page = self._page("controle")
        remote = self._card(page)
        remote.pack(fill="both", expand=True)
        head = tk.Frame(remote, bg=PANEL)
        head.pack(fill="x", pady=(0, 8))
        tk.Label(head, text="Controle remoto", bg=PANEL, fg=TEXT,
                 font=(FONT, 16, "bold")).pack(side="left")
        self._button(head, "Voltar", lambda: self.press("KEY_RETURN"), kind="subtle").pack(side="right")
        dpad = tk.Frame(remote, bg=PANEL)
        dpad.pack(pady=2)
        self._button(dpad, "▲", lambda: self.press("KEY_UP"), kind="nav", width=5).grid(row=0, column=1, padx=4, pady=3)
        self._button(dpad, "◀", lambda: self.press("KEY_LEFT"), kind="nav", width=5).grid(row=1, column=0, padx=4, pady=3)
        self._button(dpad, "OK", lambda: self.press("KEY_ENTER"), kind="select", width=6).grid(row=1, column=1, padx=4, pady=3)
        self._button(dpad, "▶", lambda: self.press("KEY_RIGHT"), kind="nav", width=5).grid(row=1, column=2, padx=4, pady=3)
        self._button(dpad, "▼", lambda: self.press("KEY_DOWN"), kind="nav", width=5).grid(row=2, column=1, padx=4, pady=3)
        keys = (("Mudo", "KEY_MUTE"), ("VOL +", "KEY_VOLUP"), ("VOL −", "KEY_VOLDOWN"),
                ("CH +", "KEY_CHUP"), ("CH −", "KEY_CHDOWN"), ("Menu", "KEY_MENU"),
                ("Fonte", "KEY_SOURCE"), ("Info", "KEY_INFO"), ("Config", "KEY_SETTINGS"),
                ("Lista CH", "KEY_CH_LIST"), ("Guide", "KEY_GUIDE"), ("Exit", "KEY_EXIT"))
        grid = tk.Frame(remote, bg=PANEL)
        grid.pack(fill="x", pady=(13, 7))
        for i, (label, key) in enumerate(keys):
            self._button(grid, label, lambda k=key: self.press(k), kind="secondary").grid(
                row=i // 3, column=i % 3, sticky="ew", padx=3, pady=3)
        for col in range(3):
            grid.columnconfigure(col, weight=1)
        media = tk.Frame(remote, bg=PANEL)
        media.pack(fill="x", pady=5)
        for label, key in (("⏮", "KEY_REWIND"), ("▶", "KEY_PLAY"), ("Ⅱ", "KEY_PAUSE"),
                           ("⏭", "KEY_FF"), ("■", "KEY_STOP")):
            self._button(media, label, lambda k=key: self.press(k), kind="subtle").pack(
                side="left", expand=True, fill="x", padx=3)
        pad = tk.Frame(remote, bg=PANEL)
        pad.pack(pady=(10, 0))
        number_keys = [("1", "KEY_1"), ("2", "KEY_2"), ("3", "KEY_3"),
                       ("4", "KEY_4"), ("5", "KEY_5"), ("6", "KEY_6"),
                       ("7", "KEY_7"), ("8", "KEY_8"), ("9", "KEY_9"),
                       ("−/100", "KEY_100"), ("0", "KEY_0"), ("PRE-CH", "KEY_PRECH")]
        for i, (label, key) in enumerate(number_keys):
            self._button(pad, label, lambda k=key: self.press(k), kind="subtle", width=8).grid(
                row=i // 3, column=i % 3, padx=3, pady=2)

    def select_page(self, page: str) -> None:
        for name, frame in self.pages.items():
            frame.pack_forget()
            self.tab_buttons[name].configure(
                bg=ACCENT if name == page else PANEL_2,
                fg="#08131c" if name == page else TEXT)
        self.pages[page].pack(fill="both", expand=True)

    def open_streaming_app(self, name: str) -> None:
        self.run_action(f"abrir {name}", lambda: launch_app(name))

    def _card(self, parent: tk.Widget) -> tk.Frame:
        return tk.Frame(parent, bg=PANEL, padx=15, pady=13,
                        highlightbackground="#202d46", highlightthickness=1)

    def _button(self, parent: tk.Widget, text: str, command, *, kind: str, width: int | None = None) -> tk.Button:
        colors = {
            "primary": (ACCENT, "#09151b", ACCENT),
            "app": ("#27364c", TEXT, "#32445f"),
            "secondary": (PANEL_2, TEXT, PANEL_2),
            "subtle": (PANEL, MUTED, "#263653"),
            "nav": (PANEL_2, TEXT, PANEL_2),
            "select": (ACCENT, "#0c1822", ACCENT),
            "danger": ("#3b1e30", RED, "#54233b"),
        }
        bg, fg, active = colors[kind]
        options = {"text": text, "command": command, "bg": bg, "fg": fg,
                   "activebackground": active, "activeforeground": fg,
                   "relief": "flat", "bd": 0, "padx": 11, "pady": 8,
                   "font": (FONT, 9, "bold"), "cursor": "hand2",
                   "highlightthickness": 0}
        if width is not None:
            options["width"] = width
        return tk.Button(parent, **options)

    def _build_tray(self) -> None:
        # pystray tenta registrar WM_TASKBARCREATED para processos elevados.
        # O registro pode retornar ERROR_ACCESS_DENIED em sessões Windows
        # restritas; para este app, que não é elevado, essa mensagem é opcional.
        if sys.platform == "win32":
            try:
                import pystray._win32
                pystray._win32.win32.ChangeWindowMessageFilterEx = lambda *args: True
            except (AttributeError, ImportError):
                pass
        menu = pystray.Menu(
            pystray.MenuItem("Abrir controle", lambda icon, item: self.show_window(), default=True),
            pystray.MenuItem("Silenciar / ativar som", lambda icon, item: self.tray_action("KEY_MUTE")),
            pystray.MenuItem("Desligar TV", lambda icon, item: self.tray_power_off()),
            pystray.Menu.SEPARATOR,
            pystray.MenuItem("Sair", lambda icon, item: self.quit()),
        )
        self.tray = pystray.Icon("samsung_tv_remote", make_tray_image(), APP_NAME, menu)
        threading.Thread(target=self.tray.run, daemon=True).start()

    def show_window(self) -> None:
        self.root.after(0, self._show_window)

    def _show_window(self) -> None:
        self.root.deiconify()
        self.root.lift()
        self.root.focus_force()

    def hide_window(self) -> None:
        self.root.withdraw()
        self.message.set("Controle minimizado. Abra pelo ícone da Samsung na bandeja.")

    def run_action(self, label: str, operation) -> None:
        self.message.set(f"Enviando comando para {label}…")

        def worker() -> None:
            try:
                operation()
                result = f"Comando para {label} enviado."
            except Exception as exc:
                result = f"Falha: {exc}"
            self.root.after(0, lambda: self.message.set(result))
            self.root.after(0, self.refresh_status)

        threading.Thread(target=worker, daemon=True).start()

    def press(self, key: str) -> None:
        self.run_action(key.replace("KEY_", "").replace("_", " ").lower(), lambda: send_key(key))

    def tray_action(self, key: str) -> None:
        self.press(key)

    def tray_power_off(self) -> None:
        self.run_action("desligar", power_off_tv)

    def refresh_status(self) -> None:
        self.state_text.set("Verificando conexão…")

        def worker() -> None:
            try:
                device = get_tv_status()
                power = device.get("PowerState", "desconhecido")
                model = device.get("modelName", "TV Samsung")
                title = "TV ligada" if power == "on" else "TV em espera" if power == "standby" else "TV indisponível"
                color = ACCENT if power == "on" else BLUE if power == "standby" else RED
                self.root.after(0, lambda: self.state_text.set(title))
                self.root.after(0, lambda: self.model_text.set(model))
                self.root.after(0, lambda: self.content_title.set("Conteúdo atual não identificado"))
                self.root.after(0, lambda: self.content_subtitle.set(
                    "A TV está conectada. Títulos e andamento dependem dos metadados que cada app fornece."
                ))
                self.root.after(0, lambda: self.status_dot.set("●"))
                self.root.after(0, lambda: self._paint_status(color))
            except Exception:
                self.root.after(0, lambda: self.state_text.set("TV não encontrada na rede"))
                self.root.after(0, lambda: self.model_text.set("Samsung TV"))
                self.root.after(0, lambda: self.content_title.set("Sem conexão"))
                self.root.after(0, lambda: self.content_subtitle.set(
                    "Confira se a TV está ligada e se o IP no arquivo .env continua correto."
                ))
                self.root.after(0, lambda: self._paint_status(RED))

        threading.Thread(target=worker, daemon=True).start()

    def _paint_status(self, color: str) -> None:
        # The status dot is a StringVar; color its shared label via the panel widgets.
        for child in self.root.winfo_children():
            self._recolor_dot(child, color)

    def _recolor_dot(self, widget: tk.Widget, color: str) -> None:
        if isinstance(widget, tk.Label) and str(widget.cget("textvariable")) == str(self.status_dot):
            widget.configure(fg=color)
        for child in widget.winfo_children():
            self._recolor_dot(child, color)

    def refresh_loop(self) -> None:
        self.refresh_status()
        self.root.after(30000, self.refresh_loop)

    @staticmethod
    def startup_enabled() -> bool:
        if sys.platform != "win32":
            return False
        try:
            import winreg
            with winreg.OpenKey(winreg.HKEY_CURRENT_USER, r"Software\Microsoft\Windows\CurrentVersion\Run") as key:
                winreg.QueryValueEx(key, "SamsungTVRemote")
            return True
        except (OSError, ImportError):
            return False

    def toggle_startup(self) -> None:
        if sys.platform != "win32":
            self.startup_var.set(False)
            return
        try:
            import winreg
            key_path = r"Software\Microsoft\Windows\CurrentVersion\Run"
            with winreg.OpenKey(winreg.HKEY_CURRENT_USER, key_path, 0, winreg.KEY_SET_VALUE) as key:
                if self.startup_var.get():
                    if getattr(sys, "frozen", False):
                        command = f'"{sys.executable}" --tray'
                    else:
                        pythonw = Path(sys.executable).with_name("pythonw.exe")
                        executable = pythonw if pythonw.exists() else Path(sys.executable)
                        command = f'"{executable}" "{Path(__file__).resolve()}" --tray'
                    winreg.SetValueEx(key, "SamsungTVRemote", 0, winreg.REG_SZ, command)
                else:
                    try:
                        winreg.DeleteValue(key, "SamsungTVRemote")
                    except FileNotFoundError:
                        pass
            self.message.set("Preferência de inicialização atualizada.")
        except OSError as exc:
            self.startup_var.set(not self.startup_var.get())
            messagebox.showerror("Inicialização do Windows", str(exc))

    def quit(self) -> None:
        try:
            self.tray.stop()
        finally:
            self.root.after(0, self.root.destroy)

    def run(self) -> None:
        self.root.mainloop()


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--tray", action="store_true", help="Iniciar minimizado na bandeja")
    args = parser.parse_args()
    app = TVApp()
    if args.tray:
        app.root.withdraw()
    app.run()
