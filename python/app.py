from __future__ import annotations

import tkinter as tk
from pathlib import Path
from tkinter import filedialog, messagebox, ttk

import qrcode
from PIL import ImageTk

from qr_payload import encode_project_chunks, make_project, project_json_bytes


APP_TITLE = "PLC IO Checker Project Builder Python"
QR_ERROR_CORRECTION_LEVELS = {
    "L low 7%": qrcode.constants.ERROR_CORRECT_L,
    "M medium 15%": qrcode.constants.ERROR_CORRECT_M,
    "Q quartile 25%": qrcode.constants.ERROR_CORRECT_Q,
    "H high 30%": qrcode.constants.ERROR_CORRECT_H,
}


class PlcQrApp(tk.Tk):
    def __init__(self) -> None:
        super().__init__()
        self.title(APP_TITLE)
        self.geometry("1400x900")
        self.chunks = []
        self.qr_images = []
        self.current_index = 0
        self._build_ui()
        self.bind_all("<Left>", self._handle_qr_page_key, add="+")
        self.bind_all("<Right>", self._handle_qr_page_key, add="+")

    def _build_ui(self) -> None:
        root = ttk.PanedWindow(self, orient=tk.HORIZONTAL)
        root.pack(fill=tk.BOTH, expand=True, padx=10, pady=10)

        form = ttk.Frame(root)
        preview = ttk.Frame(root)
        root.add(form, weight=3)
        root.add(preview, weight=2)

        self.project_name = self._entry(form, "projectName", "PLC QR Project", 0)
        self.vendor = self._combo(form, "plc.vendor", ["MELSEC", "KEYENCE"], "MELSEC", 1)
        self.connection_mode = self._combo(form, "plc.connection.mode", ["REAL", "DEMO_MOCK"], "REAL", 2)
        self.host = self._entry(form, "plc.connection.host", "192.168.250.100", 3)
        self.port = self._entry(form, "plc.connection.port", "1025", 4)
        self.cpu_model = self._combo(form, "plc.cpuModel", ["iQ-R", "iQ-F", "iQ-L", "MX-R", "MX-F", "QnUDV", "QnU", "QCPU", "LCPU", "KV-X500", "KV-8000", "KV-7000", "KV-5000"], "iQ-R", 5)
        self.keyence_mode = self._combo(form, "plc.keyence.deviceMode", ["NORMAL", "XYM"], "NORMAL", 6)
        self.transport = self._combo(form, "plc.connection.transport", ["TCP", "UDP"], "TCP", 7)
        self.polling_interval = self._entry(form, "plc.connection.pollingIntervalMs", "500", 8)
        self.timeout = self._entry(form, "plc.connection.timeoutMs", "2000", 9)
        self.network_no = self._entry(form, "plc.melsec.networkNo", "0", 10)
        self.station_no = self._entry(form, "plc.melsec.stationNo", "255", 11)
        self.module_io_no = self._entry(form, "plc.melsec.moduleIoNo", "1023", 12)
        self.multidrop_no = self._entry(form, "plc.melsec.multidropNo", "0", 13)
        self.chunk_size = self._entry(form, "QR chunk chars", "800", 14)
        self.qr_display_size = self._entry(form, "QR display px", "1000", 15)
        self.qr_error_correction = self._combo(form, "QR correction", list(QR_ERROR_CORRECTION_LEVELS.keys()), "L low 7%", 16)

        text_area = ttk.Frame(form)
        text_area.grid(row=17, column=0, columnspan=2, sticky="nsew", pady=(10, 0))
        form.rowconfigure(17, weight=1)
        form.columnconfigure(1, weight=1)
        for index in range(3):
            text_area.columnconfigure(index, weight=1)

        self.device_list_text = self._text_box(
            text_area,
            "deviceList: address,dataType,comment",
            "X000,BIT,Start input\nY000,BIT,Run output\nD100,INT16,Speed\nD102,UINT32,Counter\n",
            0,
        )
        self.time_chart_text = self._text_box(
            text_area,
            "timeChart: address,dataType",
            "X000,BIT\nD100,INT16\n",
            1,
        )
        self.traps_text = self._text_box(
            text_area,
            "traps: address,dataType,condition,comparisonValue,enabled",
            "D100,INT16,GREATER_OR_EQUAL,100,true\nD102,UINT32,CHANGE,,true\n",
            2,
        )

        button_row = ttk.Frame(form)
        button_row.grid(row=18, column=0, columnspan=2, sticky="ew", pady=10)
        ttk.Button(button_row, text="Generate QR", command=self.generate).pack(side=tk.LEFT, padx=(0, 8))
        ttk.Button(button_row, text="Save JSON", command=self.save_json).pack(side=tk.LEFT, padx=(0, 8))
        ttk.Button(button_row, text="Save QR PNG", command=self.save_qr_images).pack(side=tk.LEFT)

        self.qr_label = ttk.Label(preview)
        self.qr_label.pack(pady=(0, 10))
        self.page_label = ttk.Label(preview, text="QR not generated")
        self.page_label.pack()

        nav = ttk.Frame(preview)
        nav.pack(pady=10)
        ttk.Button(nav, text="Prev", command=self.prev_qr).pack(side=tk.LEFT, padx=6)
        ttk.Button(nav, text="Next", command=self.next_qr).pack(side=tk.LEFT, padx=6)

        ttk.Label(preview, text="QR text").pack(anchor=tk.W, pady=(10, 0))
        self.qr_text = tk.Text(preview, height=8, wrap=tk.WORD)
        self.qr_text.pack(fill=tk.X)

        ttk.Label(preview, text="Project JSON v2").pack(anchor=tk.W, pady=(10, 0))
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

    def _handle_qr_page_key(self, event: tk.Event) -> str | None:
        if not self.chunks or self._is_text_input(event.widget):
            return None
        if event.keysym == "Left":
            self.prev_qr()
            return "break"
        if event.keysym == "Right":
            self.next_qr()
            return "break"
        return None

    @staticmethod
    def _is_text_input(widget: tk.Misc) -> bool:
        return widget.winfo_class() in {"Entry", "TEntry", "Text", "Combobox", "TCombobox", "Spinbox", "TSpinbox"}

    def build_project(self) -> dict:
        return make_project(
            project_name=self.project_name.get().strip() or "PLC QR Project",
            vendor=self.vendor.get(),
            connection_mode=self.connection_mode.get(),
            host=self.host.get().strip(),
            port=self._int_value(self.port, default=1025, minimum=1, maximum=65535),
            polling_interval_ms=self._int_value(self.polling_interval, default=500, minimum=50, maximum=60000),
            timeout_ms=self._int_value(self.timeout, default=2000, minimum=100, maximum=60000),
            cpu_model=self.cpu_model.get(),
            keyence_device_mode=self.keyence_mode.get(),
            transport=self.transport.get(),
            network_no=self._int_value(self.network_no, default=0, minimum=0, maximum=255),
            station_no=self._int_value(self.station_no, default=255, minimum=0, maximum=255),
            module_io_no=self._int_value(self.module_io_no, default=1023, minimum=0, maximum=65535),
            multidrop_no=self._int_value(self.multidrop_no, default=0, minimum=0, maximum=255),
            device_list_text=self.device_list_text.get("1.0", tk.END),
            time_chart_text=self.time_chart_text.get("1.0", tk.END),
            traps_text=self.traps_text.get("1.0", tk.END),
        )

    def generate(self) -> None:
        try:
            project = self.build_project()
            chunk_size = self._int_value(self.chunk_size, default=800, minimum=200, maximum=2400)
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
        display_size = self._int_value(self.qr_display_size, default=1000, minimum=240, maximum=1400)
        image.thumbnail((display_size, display_size))
        return ImageTk.PhotoImage(image)

    def show_current_qr(self) -> None:
        if not self.chunks:
            return
        chunk = self.chunks[self.current_index]
        self.qr_label.configure(image=self.qr_images[self.current_index])
        self.page_label.configure(
            text=f"{chunk.index}/{chunk.total}  chars={len(chunk.payload)}  zstd  ec={self.qr_error_correction.get()}  session={chunk.session}"
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
