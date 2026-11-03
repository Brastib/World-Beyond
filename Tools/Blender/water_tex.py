# World Beyond: eigene, kachelbare Wasser-Texturen (keine fremden Bilder). Alles per FFT erzeugt und daher nahtlos.
import numpy as np, sys, os
from PIL import Image, ImageFilter
OUT = sys.argv[1]; rng = np.random.default_rng(42)
def spectrum_height(S, k_peak, k_width, dir_angle, dir_spread, seed):
    r = np.random.default_rng(seed); f = np.fft.fftfreq(S) * S
    kx, ky = np.meshgrid(f, f); k = np.hypot(kx, ky) + 1e-6; ang = np.arctan2(ky, kx)
    amp = np.exp(-((np.log(k) - np.log(k_peak)) ** 2) / (2 * k_width ** 2)) / k
    amp *= (np.cos(ang - dir_angle) ** 2) ** dir_spread + 0.15          # Hauptrichtung (Wind), etwas Gegenwelle
    h = np.real(np.fft.ifft2(amp * np.exp(2j * np.pi * r.random((S, S)))))
    return (h - h.mean()) / h.std()
def normal_map(h, strength):
    gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) / 2; gy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) / 2
    n = np.stack([-gx * strength, gy * strength, np.ones_like(h)], -1); n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return ((n * 0.5 + 0.5) * 255 + 0.5).astype(np.uint8)
S = 1024
fine = spectrum_height(S, 38, 0.45, 0.4, 1.0, 1) + 0.5 * spectrum_height(S, 80, 0.35, 0.9, 0.5, 2)
coarse = spectrum_height(S, 6, 0.4, 0.4, 2.0, 3) + 0.35 * spectrum_height(S, 13, 0.4, 0.1, 1.0, 4)
Image.fromarray(normal_map(fine, 6.0)).save(os.path.join(OUT, "T_WaterFine_N.png"))
Image.fromarray(normal_map(coarse, 9.0)).save(os.path.join(OUT, "T_WaterCoarse_N.png"))
# Schaummaske: Schaumflocken und Schlieren (weiß = Schaum), weicher Übergang für Uferschaum per Schwellwert im Shader
b = spectrum_height(S, 60, 0.5, 0.0, 0.0, 5); c = spectrum_height(S, 10, 0.5, 0.4, 1.0, 6); d = spectrum_height(S, 160, 0.3, 0.0, 0.0, 7)
foam = 0.55 * c + 0.35 * b + 0.25 * d
foam = np.clip((foam - 0.1) * 0.9 + 0.5, 0, 1) ** 1.6
cells = np.clip(1 - np.abs(spectrum_height(S, 48, 0.25, 0, 0, 8)) * 1.6, 0, 1) ** 3      # Blasenränder
foam = np.clip(foam * 0.75 + cells * 0.45 * foam ** 0.5, 0, 1)
Image.fromarray((foam * 255).astype(np.uint8), "L").save(os.path.join(OUT, "T_WaterFoam_Mask.png"))
# Kaustik: Licht durch eine bewegte Oberfläche gebrochen -> Lichtdichte am Grund (Punkte verschieben und zählen, mit Umlauf)
C = 512; hc = spectrum_height(C, 7, 0.35, 0, 0, 9) + 0.4 * spectrum_height(C, 14, 0.3, 1.0, 0.5, 10)
gx = (np.roll(hc, -1, 1) - np.roll(hc, 1, 1)) / 2; gy = (np.roll(hc, -1, 0) - np.roll(hc, 1, 0)) / 2
acc = np.zeros((C, C)); sub = 4
for oy in range(sub):
    for ox in range(sub):
        yy, xx = np.mgrid[0:C, 0:C].astype(float); yy += oy / sub; xx += ox / sub
        tx = (xx + gx * 55.0) % C; ty = (yy + gy * 55.0) % C
        np.add.at(acc, (ty.astype(int), tx.astype(int)), 1.0)
img = Image.fromarray(np.clip(acc / acc.mean() * 60, 0, 255).astype(np.uint8), "L")
# nahtlos weichzeichnen: 3x3 kacheln, blurren, Mitte ausschneiden
big = Image.new("L", (C * 3, C * 3))
for i in range(3):
    for j in range(3): big.paste(img, (i * C, j * C))
big = big.filter(ImageFilter.GaussianBlur(1.6)).crop((C, C, 2 * C, 2 * C))
a = np.asarray(big).astype(float); a = np.clip((a - a.mean() * 0.8) / (a.max() - a.mean() * 0.8), 0, 1) ** 1.3
Image.fromarray((a * 255).astype(np.uint8), "L").save(os.path.join(OUT, "T_WaterCaustics.png"))
# Kontrollbild: je Textur 2x2 gekachelt (Nahtprüfung)
def tile(path):
    im = Image.open(path).convert("RGB").resize((256, 256)); t = Image.new("RGB", (512, 512))
    for i in range(2):
        for j in range(2): t.paste(im, (i * 256, j * 256))
    return t
sheet = Image.new("RGB", (2048, 512))
for i, n in enumerate(["T_WaterFine_N.png", "T_WaterCoarse_N.png", "T_WaterFoam_Mask.png", "T_WaterCaustics.png"]): sheet.paste(tile(os.path.join(OUT, n)), (i * 512, 0))
sheet.save(os.path.join(OUT, "_check.jpg"), quality=85)
print("WATER ok")
