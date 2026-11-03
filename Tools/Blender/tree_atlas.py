# World Beyond: Blatt-Atlanten für Bäume, in Blender aus echten 3D-Zweigen gerendert (keine fremden Bilder).
# Aufruf: blender -b -noaudio --python tree_atlas.py -- <broad|cherry|blossom|needle> <outdir> [samples]
# Ergebnis: T_Leaf<Art>_D.png (Farbe + Alpha), _N.png (Normal Map, OpenGL), _MS.png (Glätte im Alpha).
# Atlas 2 x 2 Felder; jedes Feld zeigt einen Zweig, der unten in der Mitte ansetzt und nach oben wächst.
import bpy, bmesh, sys, math, random, os
from mathutils import Vector, Matrix
import numpy as np

a = sys.argv[sys.argv.index("--") + 1:]
KIND, OUT = a[0], a[1]; SAMPLES = int(a[2]) if len(a) > 2 else 64
CELL_PX = 512
rng = random.Random({"broad": 11, "cherry": 12, "blossom": 13, "needle": 14}[KIND])
bpy.ops.wm.read_factory_settings(use_empty=True); sc = bpy.context.scene

def srgb_to_lin(c): return tuple(((x / 255) ** 2.2) for x in c) + (1,)
def principled(name, col, rough=0.55, sss=0.0):
    m = bpy.data.materials.new(name); m.use_nodes = True; b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = srgb_to_lin(col); b.inputs["Roughness"].default_value = rough
    nt = m.node_tree; tc = nt.nodes.new("ShaderNodeTexCoord"); nz = nt.nodes.new("ShaderNodeTexNoise")
    nz.inputs["Scale"].default_value = 60.0; nz.inputs["Detail"].default_value = 4.0
    nt.links.new(tc.outputs["Object"], nz.inputs["Vector"])
    hsv = nt.nodes.new("ShaderNodeHueSaturation"); hsv.inputs["Color"].default_value = srgb_to_lin(col)
    mr = nt.nodes.new("ShaderNodeMapRange"); mr.inputs["To Min"].default_value = 0.85; mr.inputs["To Max"].default_value = 1.15
    nt.links.new(nz.outputs["Fac"], mr.inputs["Value"]); nt.links.new(mr.outputs["Result"], hsv.inputs["Value"])
    nt.links.new(hsv.outputs["Color"], b.inputs["Base Color"])   # leichte Fleckigkeit statt Einheitsfarbe
    return m

# Farben (sRGB) je Art: mehrere Töne, damit der Zweig nicht einfarbig wirkt
PAL = {
    "broad": [(64, 92, 34), (78, 104, 38), (52, 80, 30), (92, 112, 44), (70, 86, 30)],
    "cherry": [(70, 98, 36), (84, 108, 40), (60, 88, 32), (96, 116, 46)],
    "blossom": [(246, 222, 228), (250, 232, 236), (240, 206, 216), (252, 240, 242)],
    "needle": [(52, 80, 42), (62, 90, 48), (46, 72, 38), (72, 98, 52)],
}[KIND]
LEAF_MATS = [principled(f"leaf{i}", c, 0.5) for i, c in enumerate(PAL)]
STEM = principled("stem", (78, 58, 40) if KIND != "needle" else (92, 64, 42), 0.8)
CENTER = principled("center", (226, 196, 90), 0.7)          # Staubgefäße der Blüte
CHERRY_LEAF = [principled(f"cl{i}", c, 0.5) for i, c in enumerate([(84, 112, 42), (98, 120, 48), (72, 100, 38)])]

def add_obj(bm, mats):
    me = bpy.data.meshes.new("m"); bm.to_mesh(me); bm.free()
    for m in mats: me.materials.append(m)
    o = bpy.data.objects.new("o", me); sc.collection.objects.link(o); return o

def stem(bm, pts, r0, r1, sides=6, mi=0):
    rings = []
    for i, p in enumerate(pts):
        t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        u = t.orthogonal().normalized(); v = t.cross(u)
        r = r0 + (r1 - r0) * i / (len(pts) - 1)
        rings.append([bm.verts.new(p + (u * math.cos(2 * math.pi * k / sides) + v * math.sin(2 * math.pi * k / sides)) * r) for k in range(sides)])
    for i in range(len(rings) - 1):
        for k in range(sides):
            f = bm.faces.new((rings[i][k], rings[i][(k + 1) % sides], rings[i + 1][(k + 1) % sides], rings[i + 1][k])); f.material_index = mi

def curve(p0, d, length, n, droop=0.0, wiggle=0.0):
    pts = [p0.copy()]; p = p0.copy(); d = d.normalized()
    for i in range(n):
        d = (d + Vector((rng.uniform(-wiggle, wiggle), rng.uniform(-wiggle, wiggle), -droop))).normalized()
        p = p + d * length / n; pts.append(p.copy())
    return pts

def leaf(bm, base, d, length, width, mi, up_tilt=0.25, fold=0.18, bend=0.12, serr=0.0):
    """Blatt mit Mittelrippe (V-Falz), Spitze und leichter Krümmung. d liegt etwa in der XY-Ebene."""
    d = d.normalized(); n = Vector((0, 0, 1)); s = n.cross(d).normalized(); n = d.cross(s).normalized()
    n = (n + d * -up_tilt).normalized()
    N = 7; rows = []
    for i in range(N + 1):
        t = i / N
        w = width * (math.sin(math.pi * min(t, 0.999) ** 0.85)) ** 0.9
        if serr: w *= 1 + serr * math.sin(t * 40)
        c = base + d * length * t + n * (-bend * length * t * t)
        rows.append((bm.verts.new(c + s * w / 2 + n * fold * w), bm.verts.new(c), bm.verts.new(c - s * w / 2 + n * fold * w)))
    for i in range(N):
        for j in range(2):
            f = bm.faces.new((rows[i][j], rows[i + 1][j], rows[i + 1][j + 1], rows[i][j + 1])); f.material_index = mi
    stem(bm, [base - d * 0.012, base + d * 0.004], 0.0015, 0.0012, 4, 0)  # Blattstiel

def flower(bm, c, r, mi_pet, mi_cen, tilt):
    """Kirschblüte: fünf gewölbte Blütenblätter mit Kerbe und Staubgefäßen."""
    up = Vector((tilt.x, tilt.y, 1)).normalized(); a0 = rng.uniform(0, 2 * math.pi)
    u = up.orthogonal().normalized(); v = up.cross(u)
    for k in range(5):
        ang = a0 + 2 * math.pi * k / 5; d = u * math.cos(ang) + v * math.sin(ang); s = up.cross(d).normalized()
        rows = []
        for i in range(5):
            t = i / 4; w = r * 0.95 * math.sin(math.pi * (0.15 + 0.85 * t)) ** 0.6 * (1 - 0.15 * (t == 1))
            p = c + d * r * (0.1 + 0.9 * t) + up * (r * 0.35 * t * t)
            notch = up * 0 + (d * (-0.12 * r) if i == 4 else Vector())
            rows.append((bm.verts.new(p + s * w / 2), bm.verts.new(p + notch), bm.verts.new(p - s * w / 2)))
        for i in range(4):
            for j in range(2):
                f = bm.faces.new((rows[i][j], rows[i + 1][j], rows[i + 1][j + 1], rows[i][j + 1])); f.material_index = mi_pet
    for k in range(9):
        ang = 2 * math.pi * k / 9; d = u * math.cos(ang) + v * math.sin(ang)
        tip = c + d * r * 0.45 + up * r * 0.5
        stem(bm, [c, tip], 0.0009, 0.0007, 3, 0)
        q = bmesh.ops.create_icosphere(bm, subdivisions=1, radius=r * 0.08)["verts"]
        bmesh.ops.translate(bm, vec=tip, verts=q)
        for f in {f for vv in q for f in vv.link_faces}: f.material_index = mi_cen

def twig_broad(origin, size, cherry=False):
    """Laubzweig: Hauptzweig mit Seitenzweigen, Blätter wechselständig, Blattflächen meist nach oben."""
    bm = bmesh.new(); mats = [STEM] + (CHERRY_LEAF if cherry else LEAF_MATS)
    nleafmats = len(mats) - 1
    main = curve(origin + Vector((0, 0.03 * size, 0)), Vector((rng.uniform(-0.15, 0.15), 1, 0)), size * 0.82, 10, 0.0, 0.08)
    stem(bm, main, 0.008 * size, 0.003 * size, 6)
    branches = [main]
    for k in range(rng.randint(6, 8)):
        t = rng.uniform(0.12, 0.8); i = int(t * (len(main) - 1)); p = main[i]
        side = 1 if k % 2 == 0 else -1
        d = Vector((side * rng.uniform(0.6, 1.0), rng.uniform(0.5, 0.9), 0))
        L = size * rng.uniform(0.22, 0.38) * (1.15 - t * 0.6)
        br = curve(p, d, L, 6, 0.0, 0.1); stem(bm, br, 0.005 * size, 0.002 * size, 5); branches.append(br)
    leaf_len = (0.11 if not cherry else 0.10) * size / 1.0
    for br in branches:
        for i in range(1, len(br)):
            for side in (-1, 1):
                if rng.random() < 0.12: continue
                d0 = (br[i] - br[i - 1]).normalized()
                sd = Vector((0, 0, 1)).cross(d0).normalized() * side
                d = (sd * rng.uniform(0.8, 1.4) + d0 * rng.uniform(0.3, 0.9)).normalized()
                d.z += rng.uniform(-0.15, 0.25)
                L = leaf_len * rng.uniform(0.7, 1.15)
                leaf(bm, br[i] + Vector((0, 0, rng.uniform(-0.004, 0.008))), d, L, L * (0.5 if not cherry else 0.42),
                     1 + rng.randrange(nleafmats), up_tilt=rng.uniform(-0.1, 0.35), bend=rng.uniform(0.04, 0.2), serr=0.04 if cherry else 0.0)
        # Endblatt
        d = (br[-1] - br[-2]).normalized(); leaf(bm, br[-1], d, leaf_len, leaf_len * 0.48, 1 + rng.randrange(nleafmats))
    return add_obj(bm, mats)

def twig_blossom(origin, size):
    """Blühender Kirschzweig: dichte Büschel aus drei bis fünf Blüten, wenige junge Blätter."""
    bm = bmesh.new(); mats = [STEM] + LEAF_MATS + [CENTER] + CHERRY_LEAF
    mi_cen = 1 + len(LEAF_MATS); mi_leaf0 = mi_cen + 1
    main = curve(origin + Vector((0, 0.03 * size, 0)), Vector((rng.uniform(-0.2, 0.2), 1, 0)), size * 0.85, 10, 0.0, 0.1)
    stem(bm, main, 0.009 * size, 0.004 * size, 6)
    branches = [main]
    for k in range(rng.randint(6, 8)):
        t = rng.uniform(0.1, 0.75); p = main[int(t * (len(main) - 1))]; side = 1 if k % 2 == 0 else -1
        d = Vector((side * rng.uniform(0.6, 1.0), rng.uniform(0.4, 0.9), 0))
        br = curve(p, d, size * rng.uniform(0.25, 0.4) * (1.1 - t * 0.5), 6, 0.0, 0.12); stem(bm, br, 0.006 * size, 0.0025 * size, 5); branches.append(br)
    fr = 0.032 * size
    for br in branches:
        for i in range(1, len(br)):
            if rng.random() < 0.05: continue
            p = br[i]
            for kk in range(rng.randint(4, 7)):
                ang = rng.uniform(0, 2 * math.pi); off = Vector((math.cos(ang), math.sin(ang), 0)) * fr * rng.uniform(0.9, 1.8)
                c = p + off + Vector((0, 0, rng.uniform(0.005, 0.03)))
                stem(bm, [p, c], 0.0012, 0.001, 3)
                flower(bm, c, fr * rng.uniform(0.85, 1.1), 1 + rng.randrange(len(LEAF_MATS)), mi_cen, off.normalized() * 0.5)
            if rng.random() < 0.3:
                d0 = (br[i] - br[i - 1]).normalized(); sd = Vector((0, 0, 1)).cross(d0).normalized() * rng.choice((-1, 1))
                L = 0.06 * size * rng.uniform(0.7, 1.0)
                leaf(bm, p, (sd + d0 * 0.5).normalized(), L, L * 0.42, mi_leaf0 + rng.randrange(3), bend=0.1)
    return add_obj(bm, mats)

def twig_needle(origin, size):
    """Fichtenzweig: flacher, verzweigter Wedel; Nadeln rundum, oben dichter."""
    bm = bmesh.new(); mats = [STEM] + LEAF_MATS
    main = curve(origin + Vector((0, 0.02 * size, 0)), Vector((rng.uniform(-0.1, 0.1), 1, 0)), size * 0.9, 18, 0.0, 0.04)
    stem(bm, main, 0.008 * size, 0.003 * size, 5)
    branches = [main]
    for i in range(2, len(main) - 1):
        for side in (-1, 1):
            if rng.random() < 0.1: continue
            t = i / (len(main) - 1)
            d = Vector((side * rng.uniform(0.8, 1.1), rng.uniform(0.6, 0.9), 0))
            br = curve(main[i], d, size * 0.32 * (1.05 - t * 0.7), 6, 0.0, 0.08); stem(bm, br, 0.004 * size, 0.002 * size, 4); branches.append(br)
            if t < 0.7 and rng.random() < 0.8:  # dritte Ordnung
                j = 3; d2 = Vector((side * 0.3, 1, 0)) if rng.random() < 0.5 else Vector((side * 1.0, 0.3, 0))
                b2 = curve(br[j], d2, size * 0.12, 4, 0.0, 0.1); stem(bm, b2, 0.0025 * size, 0.0015 * size, 3); branches.append(b2)
    nl = 0.05 * size  # für die Texturauflösung etwas länger und dicker als echt (sonst unter 1 Pixel)
    for br in branches:
        total = sum((br[i] - br[i - 1]).length for i in range(1, len(br)))
        steps = int(total / (0.003 * size))
        for s in range(steps):
            f = s / max(1, steps - 1) * (len(br) - 1); i = min(int(f), len(br) - 2); p = br[i].lerp(br[i + 1], f - i)
            d0 = (br[i + 1] - br[i]).normalized()
            for k in range(rng.randint(5, 7)):
                ang = rng.uniform(-math.pi, math.pi)
                if abs(ang) > 2.4 and rng.random() < 0.6: continue  # unten weniger Nadeln
                side = Vector((0, 0, 1)).cross(d0).normalized()
                r = (side * math.cos(ang) + Vector((0, 0, 1)) * math.sin(ang) * 0.8 + d0 * 0.55).normalized()
                L = nl * rng.uniform(0.75, 1.15); tip = p + r * L
                w = 0.0022 * size; q = d0.cross(r).normalized() * w
                v1, v2, v3, v4 = bm.verts.new(p + q), bm.verts.new(p - q), bm.verts.new(tip - q * 0.3), bm.verts.new(tip + q * 0.3)
                ff = bm.faces.new((v1, v2, v3, v4)); ff.material_index = 1 + rng.randrange(len(LEAF_MATS))
    return add_obj(bm, mats)

SIZE = 1.0
objs = []
for cell in range(4):
    origin = Vector(((cell % 2) * 3.0, (cell // 2) * 3.0, 0))
    o = {"broad": lambda: twig_broad(origin, SIZE), "cherry": lambda: twig_broad(origin, SIZE, True),
         "blossom": lambda: twig_blossom(origin, SIZE), "needle": lambda: twig_needle(origin, SIZE)}[KIND]()
    objs.append(o)
    # in das Feld einpassen: alles muss innerhalb des Quadrats (Zweigansatz unten Mitte) liegen
    bb = [o.matrix_world @ Vector(c) for c in o.bound_box]
    w = max(max(v.x for v in bb) - origin.x, origin.x - min(v.x for v in bb)) * 2; h = max(v.y for v in bb) - origin.y
    k = 0.97 * SIZE / max(w, h, 1e-6)
    if k < 1:
        o.data.transform(Matrix.Translation(origin) @ Matrix.Scale(k, 4) @ Matrix.Translation(-origin))

# Licht: gleichmäßiger Himmel (ergibt Farbe mit leichter Verschattung = AO)
world = bpy.data.worlds.new("W"); sc.world = world; world.use_nodes = True
world.node_tree.nodes["Background"].inputs["Color"].default_value = (1, 1, 1, 1); world.node_tree.nodes["Background"].inputs["Strength"].default_value = 1.0
cam = bpy.data.cameras.new("C"); cam.type = "ORTHO"; cam.ortho_scale = SIZE
co = bpy.data.objects.new("C", cam); sc.collection.objects.link(co); sc.camera = co
sc.render.engine = "CYCLES"; sc.cycles.device = "CPU"; sc.cycles.use_denoising = False
sc.render.film_transparent = True; sc.render.resolution_x = sc.render.resolution_y = CELL_PX
sc.render.image_settings.file_format = "PNG"; sc.render.image_settings.color_mode = "RGBA"

nmat = bpy.data.materials.new("normal"); nmat.use_nodes = True; nt = nmat.node_tree
for n in list(nt.nodes): nt.nodes.remove(n)
geo = nt.nodes.new("ShaderNodeNewGeometry"); vm = nt.nodes.new("ShaderNodeVectorMath"); vm.operation = "MULTIPLY_ADD"
vm.inputs[1].default_value = (0.5, 0.5, 0.5); vm.inputs[2].default_value = (0.5, 0.5, 0.5)
em = nt.nodes.new("ShaderNodeEmission"); outn = nt.nodes.new("ShaderNodeOutputMaterial")
nt.links.new(geo.outputs["Normal"], vm.inputs[0]); nt.links.new(vm.outputs[0], em.inputs["Color"]); nt.links.new(em.outputs[0], outn.inputs["Surface"])

tmp = os.path.join(OUT, "_cells"); os.makedirs(tmp, exist_ok=True)
for cell in range(4):
    origin = Vector(((cell % 2) * 3.0, (cell // 2) * 3.0, 0))
    co.location = origin + Vector((0, SIZE / 2, 5))
    for pas in ("col", "nor"):
        if pas == "col":
            bpy.context.view_layer.material_override = None; sc.cycles.samples = SAMPLES; sc.view_settings.view_transform = "Standard"
        else:
            bpy.context.view_layer.material_override = nmat; sc.cycles.samples = 16; sc.view_settings.view_transform = "Raw"
        sc.render.filepath = os.path.join(tmp, f"{pas}{cell}.png"); bpy.ops.render.render(write_still=True)

# Zusammensetzen + Farbe in transparente Bereiche ausbluten lassen (sonst dunkle Säume bei Mipmaps)
from PIL import Image
def bleed(rgb, a, it=24):
    rgb = rgb.copy(); known = a > 0.5
    for _ in range(it):
        acc = np.zeros_like(rgb); cnt = np.zeros(a.shape)
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1)):
            k = np.roll(np.roll(known, dy, 0), dx, 1); acc += np.roll(np.roll(rgb, dy, 0), dx, 1) * k[..., None]; cnt += k
        new = (~known) & (cnt > 0); rgb[new] = acc[new] / cnt[new][:, None]; known = known | new
    rest = ~known
    if rest.any(): rgb[rest] = rgb[known].mean(0)
    return rgb
S = CELL_PX * 2
D = np.zeros((S, S, 4)); Nn = np.zeros((S, S, 3))
for cell in range(4):
    c = np.asarray(Image.open(os.path.join(tmp, f"col{cell}.png")).convert("RGBA")).astype(np.float64) / 255
    n = np.asarray(Image.open(os.path.join(tmp, f"nor{cell}.png")).convert("RGBA")).astype(np.float64) / 255
    x0 = (cell % 2) * CELL_PX; y0 = (1 - cell // 2) * CELL_PX   # Bild-y läuft nach unten
    al = c[..., 3]
    rgb = c[..., :3].copy()  # PNG aus Blender hat bereits unvormultipliziertes Alpha
    D[y0:y0 + CELL_PX, x0:x0 + CELL_PX, :3] = bleed(rgb, al); D[y0:y0 + CELL_PX, x0:x0 + CELL_PX, 3] = al
    nb = bleed(n[..., :3].copy(), n[..., 3]); nb[n[..., 3] < 0.02] = (0.5, 0.5, 1.0)
    Nn[y0:y0 + CELL_PX, x0:x0 + CELL_PX] = nb
if KIND == "needle":  # Nadeln verschatten sich im Render stark: aufhellen und Normalen abflachen, sonst wirkt die Fichte schwarz
    D[..., :3] = np.clip(D[..., :3] * 1.55, 0, 1); Nn = Nn * 0.35 + np.array((0.5, 0.5, 1.0)) * 0.65
name = {"broad": "Broad", "cherry": "Cherry", "blossom": "CherryBlossom", "needle": "Needle"}[KIND]
Image.fromarray((D * 255 + 0.5).astype(np.uint8), "RGBA").save(os.path.join(OUT, f"T_Leaf{name}_D.png"))
Image.fromarray((Nn * 255 + 0.5).astype(np.uint8), "RGB").save(os.path.join(OUT, f"T_Leaf{name}_N.png"))
ms = np.zeros((S, S, 4), np.uint8); ms[..., 3] = int(255 * (0.45 if KIND != "needle" else 0.35))
Image.fromarray(ms, "RGBA").save(os.path.join(OUT, f"T_Leaf{name}_MS.png"))
print("ATLAS", name, "ok")
