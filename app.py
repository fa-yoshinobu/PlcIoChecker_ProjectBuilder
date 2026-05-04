from __future__ import annotations

import json
import tkinter as tk
from pathlib import Path
from tkinter import filedialog, messagebox, ttk

import qrcode
from PIL import ImageTk

from qr_payload import encode_project_chunks, make_project, project_json_bytes


APP_TITLE = "PlcIoChecker QR"
QR_ERROR_CORRECTION_LEVELS = {
    "L 読取優先": qrcode.constants.ERROR_CORRECT_L,
    "M 標準": qrcode.constants.ERROR_CORRECT_M,
    "Q 補正強め": qrcode.constants.ERROR_CORRECT_Q,
    "H 補正最大": qrcode.constants.ERROR_CORRECT_H,
}


class PlcQrApp(tk.Tk):
    def __init__(self) -> None:
        super().__init__()
        self.title(APP_TITLE)
        self.geometry("1180x760")
        self.chunks = []
        self.qr_images = []
        self.current_index = 0
        self._build_ui()

    def _build_ui(self) -> None:
        root = ttk.PanedWindow(self, orient=tk.HORIZONTAL)
        root.pack(fill=tk.BOTH, expand=True, padx=10, pady=10)

        form = ttk.Frame(root)
        preview = ttk.Frame(root)
        root.add(form, weight=3)
        root.add(preview, weight=2)

        self.project_name = self._entry(form, "案件名", "PLC QR Project", 0)
        self.vendor = self._combo(form, "Vendor", ["Melsec", "Keyence"], "Melsec", 1)
        self.connection_mode = self._combo(form, "接続モード", ["Real", "DemoMock"], "Real", 2)
        self.host = self._entry(form, "IP", "192.168.250.100", 3)
        self.port = self._entry(form, "Port", "1025", 4)
        self.model = self._combo(form, "CPU機種", ["iQ-R", "iQ-F", "iQ-L", "MX-R", "MX-F", "QnUDV", "QnU", "QCPU", "LCPU", "KV-X500", "KV-8000", "KV-7000", "KV-5000"], "iQ-R", 5)
        self.keyence_mode = self._combo(form, "KEYENCE表示", ["Normal", "Xym"], "Normal", 6)
        self.transport = self._combo(form, "Transport", ["Tcp", "Udp"], "Tcp", 7)
        self.interval = self._entry(form, "監視周期ms", "500", 8)
        self.timeout = self._entry(form, "Timeout ms", "2000", 9)
        self.network = self._entry(form, "Network", "0", 10)
        self.station = self._entry(form, "Station", "255", 11)
        self.module_io = self._entry(form, "Module IO", "1023", 12)
        self.multidrop = self._entry(form, "Multidrop", "0", 13)
        self.chunk_size = self._entry(form, "QR 1枚の文字数", "350", 14)
        self.qr_display_size = self._entry(form, "QR表示サイズpx", "650", 15)
        self.qr_error_correction = self._combo(form, "QR誤り訂正", list(QR_ERROR_CORRECTION_LEVELS.keys()), "L 読取優先", 16)

        text_area = ttk.Frame(form)
        text_area.grid(row=17, column=0, columnspan=2, sticky="nsew", pady=(10, 0))
        form.rowconfigure(17, weight=1)
        form.columnconfigure(1, weight=1)
        for index in range(3):
            text_area.columnconfigure(index, weight=1)

        self.devices_text = self._text_box(text_area, "デバイス address,dataType", "X000,Bit\nY000,Bit\nD100,Int16\nD102,UInt32\n", 0)
        self.watch_text = self._text_box(text_area, "タイムチャート address", "X000\nD100\n", 1)
        self.traps_text = self._text_box(text_area, "トラップ address,condition,threshold,enabled", "D100,GreaterOrEqual,100,true\nD102,Change,,true\n", 2)

        button_row = ttk.Frame(form)
        button_row.grid(row=18, column=0, columnspan=2, sticky="ew", pady=10)
        ttk.Button(button_row, text="QR生成", command=self.generate).pack(side=tk.LEFT, padx=(0, 8))
        ttk.Button(button_row, text="JSON保存", command=self.save_json).pack(side=tk.LEFT, padx=(0, 8))
        ttk.Button(button_row, text="QR PNG保存", command=self.save_qr_images).pack(side=tk.LEFT)

        self.qr_label = ttk.Label(preview)
        self.qr_label.pack(pady=(0, 10))
        self.page_label = ttk.Label(preview, text="QR未生成")
        self.page_label.pack()

        nav = ttk.Frame(preview)
        nav.pack(pady=10)
        ttk.Button(nav, text="前へ", command=self.prev_qr).pack(side=tk.LEFT, padx=6)
        ttk.Button(nav, text="次へ", command=self.next_qr).pack(side=tk.LEFT, padx=6)

        ttk.Label(preview, text="QR本文").pack(anchor=tk.W, pady=(10, 0))
        self.qr_text = tk.Text(preview, height=8, wrap=tk.WORD)
        self.qr_text.pack(fill=tk.X)

        ttk.Label(preview, text="生成JSON").pack(anchor=tk.W, pady=(10, 0))
        self.json_preview = tk.Text(preview, height=14, wrap=tk.NONE)
        self.json_preview.pack(fill=tk.BOTH, expand=True)

    def _entry(self, parent: ttk.Frame, label: str, value: str, row: int) -> tk.StringVar:
        var = tk.StringVar(value=value)
        ttk.Label(parent, text=label).grid(row=row, column=0, sticky="w", padx=(0, 8), pady=2)
        ttk.Entry(parent, textvariable=var).grid(row=row, column=1, sticky="ew", pady=2)
        return var

    def _combo(self, parent: ttk.Frame, label: str, values: list[str], value: str, row: int) -> tk.StringVar:
        var = tk.StringVar(value=value)
        ttk.Label(parent, text=label).grid(row=row, column=0, sticky="w", padx=(0, 8), pady=2)
        ttk.Combobox(parent, textvariable=var, values=values, state="readonly").grid(row=row, column=1, sticky="ew", pady=2)
        return var

    def _text_box(self, parent: ttk.Frame, label: str, value: str, column: int) -> tk.Text:
        frame = ttk.Frame(parent)
        frame.grid(row=0, column=column, sticky="nsew", padx=4)
        parent.rowconfigure(0, weight=1)
        ttk.Label(frame, text=label).pack(anchor=tk.W)
        text = tk.Text(frame, height=12, wrap=tk.NONE)
        text.insert("1.0", value)
        text.pack(fill=tk.BOTH, expand=True)
        return text

    def _int_value(self, var: tk.StringVar, default: int, minimum: int, maximum: int) -> int:
        try:
            value = int(var.get())
        except ValueError:
            value = default
        return min(max(value, minimum), maximum)

    def _qr_error_correction(self) -> int:
        return QR_ERROR_CORRECTION_LEVELS.get(self.qr_error_correction.get(), qrcode.constants.ERROR_CORRECT_L)

    def build_project(self) -> dict:
        return make_project(
            name=self.project_name.get().strip() or "PLC QR Project",
            vendor=self.vendor.get(),
            connection_mode=self.connection_mode.get(),
            host=self.host.get().strip(),
            port=int(self.port.get()),
            monitor_interval_ms=int(self.interval.get()),
            timeout_ms=int(self.timeout.get()),
            machine_label=self.model.get(),
            keyence_device_mode=self.keyence_mode.get(),
            transport_mode=self.transport.get(),
            network=int(self.network.get()),
            station=int(self.station.get()),
            module_io=int(self.module_io.get()),
            multidrop=int(self.multidrop.get()),
            devices_text=self.devices_text.get("1.0", tk.END),
            watch_text=self.watch_text.get("1.0", tk.END),
            traps_text=self.traps_text.get("1.0", tk.END),
        )

    def generate(self) -> None:
        try:
            project = self.build_project()
            chunk_size = self._int_value(self.chunk_size, default=350, minimum=200, maximum=2400)
            self.chunks = encode_project_chunks(project, chunk_size=chunk_size)
            self.qr_images = [self.make_qr_image(chunk.text()) for chunk in self.chunks]
            self.current_index = 0
            self.json_preview.delete("1.0", tk.END)
            self.json_preview.insert("1.0", project_json_bytes(project).decode("utf-8"))
            self.show_current_qr()
        except Exception as error:
            messagebox.showerror(APP_TITLE, str(error))

    def make_qr_image(self, text: str) -> ImageTk.PhotoImage:
        qr = qrcode.QRCode(error_correction=self._qr_error_correction(), border=4, box_size=12)
        qr.add_data(text)
        qr.make(fit=True)
        image = qr.make_image(fill_color="black", back_color="white").convert("RGB")
        display_size = self._int_value(self.qr_display_size, default=650, minimum=240, maximum=1200)
        image.thumbnail((display_size, display_size))
        return ImageTk.PhotoImage(image)

    def show_current_qr(self) -> None:
        if not self.chunks:
            return
        chunk = self.chunks[self.current_index]
        self.qr_label.configure(image=self.qr_images[self.current_index])
        self.page_label.configure(
            text=f"{chunk.index}/{chunk.total}  chars={len(chunk.payload)}  ec={self.qr_error_correction.get()}  session={chunk.session}"
        )
        self.qr_text.delete("1.0", tk.END)
        self.qr_text.insert("1.0", chunk.text())

    def prev_qr(self) -> None:
        if not self.chunks:
            return
        self.current_index = (self.current_index - 1) % len(self.chunks)
        self.show_current_qr()

    def next_qr(self) -> None:
        if not self.chunks:
            return
        self.current_index = (self.current_index + 1) % len(self.chunks)
        self.show_current_qr()

    def save_json(self) -> None:
        try:
            project = self.build_project()
            path = filedialog.asksaveasfilename(defaultextension=".json", filetypes=[("JSON", "*.json")])
            if not path:
                return
            Path(path).write_bytes(project_json_bytes(project))
        except Exception as error:
            messagebox.showerror(APP_TITLE, str(error))

    def save_qr_images(self) -> None:
        if not self.chunks:
            self.generate()
        folder = filedialog.askdirectory()
        if not folder:
            return
        out = Path(folder)
        for chunk in self.chunks:
            qr = qrcode.QRCode(error_correction=self._qr_error_correction(), border=4, box_size=16)
            qr.add_data(chunk.text())
            qr.make(fit=True)
            image = qr.make_image(fill_color="black", back_color="white").convert("RGB")
            image.save(out / f"plcio-{chunk.session}-{chunk.index:02d}-of-{chunk.total:02d}.png")


if __name__ == "__main__":
    PlcQrApp().mainloop()
