# World Beyond: Getreide-Atlas (Seitenansicht) aus selbst modellierten 3D-Pflanzen, Blender 4.0 headless.
# Aufruf: blender -b -noaudio --python crop_atlas.py -- <outdir> [samples]
# Ergebnis: T_LeafCrop_D.png (Farbe + Alpha), _N.png (OpenGL), _MS.png. Atlas 2 x 2:
#   unten links/rechts (Felder 0, 1) = junges grünes Getreide, Höhe 0,45 m auf 0,5 m Feldbreite
#   oben links/rechts  (Felder 2, 3) = reifer Weizen mit Ähren, Höhe 1,0 m auf 1,0 m Feldbreite
# Die Karte setzt unten in der Mitte auf dem Boden an.
import bpy, bmesh, sys, math, random, os
from mathutils import Vector
import numpy as np
a = sys.argv[sys.argv.index("--") + 1:]; OUT = a[0]; SAMPLES = int(a[1]) if len(a) > 1 else 48
rng = random.Random(21); CELL_PX = 512
bpy.ops.wm.read_factory_settings(use_empty=True); sc = bpy.context.scene
def lin(c): return tuple((x / 255) ** 2.2 for x in c) + (1,)
def mat(name, col, rough=0.55):
    m = bpy.data.materials.new(name); m.use_nodes = True; b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = lin(col); b.inputs["Roughness"].default_value = rough; return m
GREEN = [mat(f"g{i}", c) for i, c in enumerate([(78, 118, 40), (92, 130, 46), (66, 104, 36), (104, 138, 54)])]
RIPE = [mat(f"r{i}", c, 0.7) for i, c in enumerate([(206, 170, 96), (190, 152, 80), (218, 186, 116), (176, 140, 74)])]
EAR = [mat(f"e{i}", c, 0.7) for i, c in enumerate([(196, 156, 82), (180, 140, 70), (210, 172, 100)])]

def blade(bm, base, length, width, lean, turn, mi, curl=0.35):
    """Grasblatt: schmaler Streifen, der sich nach außen biegt."""
    d0 = Vector((math.sin(lean) * math.cos(turn), math.sin(lean) * math.sin(turn), math.cos(lean)))
    side = Vector((-math.sin(turn), math.cos(turn), 0)); N = 8; prev = None; p = base.copy(); d = d0.copy()
    for i in range(N + 1):
        t = i / N; w = width * (1 - t ** 1.5) + 0.0005
        l = bm.verts.new(p - side * w / 2); r = bm.verts.new(p + side * w / 2)
        if prev: f = bm.faces.new((prev[0], prev[1], r, l)); f.material_index = mi
        prev = (l, r); p = p + d * (length / N)
        d = (d + Vector((math.cos(turn), math.sin(turn), 0)) * curl / N * 2 - Vector((0, 0, curl * t / N))).normalized()

def stalk(bm, base, h, lean, turn, mi_s, mi_e):
    """Halm mit Ähre und Grannen."""
    top = base + Vector((math.sin(lean) * math.cos(turn), math.sin(lean) * math.sin(turn), math.cos(lean))) * h
    rings = []
    for p in (base, base.lerp(top, 0.5), top):
        rings.append([bm.verts.new(p + Vector((math.cos(k * 2.09), math.sin(k * 2.09), 0)) * 0.0025) for k in range(3)])
    for i in range(2):
        for k in range(3):
            f = bm.faces.new((rings[i][k], rings[i][(k + 1) % 3], rings[i + 1][(k + 1) % 3], rings[i + 1][k])); f.material_index = mi_s
    d = (top - base).normalized(); droop = Vector((math.cos(turn), math.sin(turn), -0.6)) * 0.25
    for k in range(9):  # Ährchen paarweise, Ähre neigt sich leicht
        t = k / 8; c = top + (d + droop * t).normalized() * (0.012 + t * 0.075)
        for sg in (-1, 1):
            q = bmesh.ops.create_icosphere(bm, subdivisions=1, radius=0.005)["verts"]
            for v in q:
                v.co.z *= 1.6; v.co += c + Vector((-math.sin(turn), math.cos(turn), 0)) * 0.005 * sg
            for f in {f for v in q for f in v.link_faces}: f.material_index = mi_e
            tip = c + (d * 0.9 + Vector((-math.sin(turn), math.cos(turn), 0)) * 0.35 * sg).normalized() * 0.05
            v1 = bm.verts.new(c); v2 = bm.verts.new(c + Vector((0, 0, 0.001))); v3 = bm.verts.new(tip)
            f = bm.faces.new((v1, v2, v3)); f.material_index = mi_e

def plant_cell(origin, kind):
    bm = bmesh.new(); mats = GREEN if kind == "green" else RIPE + EAR
    n = 7 if kind == "green" else 9
    for k in range(n):  # mehrere Pflanzen in einer schmalen Reihe
        b = origin + Vector((rng.uniform(-0.14, 0.14) * (1 if kind == "green" else 2), rng.uniform(-0.03, 0.03), 0))
        if kind == "green":
            for j in range(rng.randint(5, 8)):
                blade(bm, b, rng.uniform(0.25, 0.42), rng.uniform(0.008, 0.012), rng.uniform(0.05, 0.45), rng.uniform(0, 6.28), rng.randrange(4), curl=rng.uniform(0.2, 0.6))
        else:
            for j in range(rng.randint(3, 5)):
                stalk(bm, b, rng.uniform(0.78, 0.9), rng.uniform(0.0, 0.14), rng.uniform(0, 6.28), rng.randrange(4), 4 + rng.randrange(3))
            for j in range(rng.randint(3, 5)):
                blade(bm, b, rng.uniform(0.3, 0.5), 0.01, rng.uniform(0.3, 0.8), rng.uniform(0, 6.28), rng.randrange(4), curl=0.5)
    me = bpy.data.meshes.new("p"); bm.to_mesh(me); bm.free()
    for m in mats: me.materials.append(m)
    o = bpy.data.objects.new("p", me); sc.collection.objects.link(o); return o

CELLS = [("green", 0.5), ("green", 0.5), ("ripe", 1.0), ("ripe", 1.0)]
for c, (kind, size) in enumerate(CELLS): plant_cell(Vector((c * 3.0, 0, 0)), kind)
w = bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True; w.node_tree.nodes["Background"].inputs["Strength"].default_value = 1.0; w.node_tree.nodes["Background"].inputs["Color"].default_value = (1, 1, 1, 1)
cam = bpy.data.cameras.new("C"); cam.type = "ORTHO"; co = bpy.data.objects.new("C", cam); sc.collection.objects.link(co); sc.camera = co
co.rotation_euler = (math.radians(90), 0, 0)  # Blick entlang +Y
sc.render.engine = "CYCLES"; sc.cycles.device = "CPU"; sc.cycles.use_denoising = False; sc.render.film_transparent = True
sc.render.resolution_x = sc.render.resolution_y = CELL_PX; sc.render.image_settings.color_mode = "RGBA"
nm = bpy.data.materials.new("n"); nm.use_nodes = True; nt = nm.node_tree
for n in list(nt.nodes): nt.nodes.remove(n)
g = nt.nodes.new("ShaderNodeNewGeometry"); vt = nt.nodes.new("ShaderNodeVectorTransform"); vt.vector_type = "NORMAL"; vt.convert_from = "WORLD"; vt.convert_to = "CAMERA"
vm = nt.nodes.new("ShaderNodeVectorMath"); vm.operation = "MULTIPLY_ADD"; vm.inputs[1].default_value = (0.5, 0.5, 0.5); vm.inputs[2].default_value = (0.5, 0.5, 0.5)
em = nt.nodes.new("ShaderNodeEmission"); oo = nt.nodes.new("ShaderNodeOutputMaterial")
nt.links.new(g.outputs["Normal"], vt.inputs["Vector"]); nt.links.new(vt.outputs["Vector"], vm.inputs[0]); nt.links.new(vm.outputs[0], em.inputs["Color"]); nt.links.new(em.outputs[0], oo.inputs["Surface"])
tmp = os.path.join(OUT, "_crop"); os.makedirs(tmp, exist_ok=True)
for c, (kind, size) in enumerate(CELLS):
    cam.ortho_scale = size; co.location = (c * 3.0, -3, size / 2)
    for pas in ("col", "nor"):
        bpy.context.view_layer.material_override = None if pas == "col" else nm
        sc.cycles.samples = SAMPLES if pas == "col" else 16
        sc.view_settings.view_transform = "Standard" if pas == "col" else "Raw"
        sc.render.filepath = os.path.join(tmp, f"{pas}{c}.png"); bpy.ops.render.render(write_still=True)
from PIL import Image
def bleed(rgb, al, it=24):
    rgb = rgb.copy(); known = al > 0.5
    for _ in range(it):
        acc = np.zeros_like(rgb); cnt = np.zeros(al.shape)
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            k = np.roll(np.roll(known, dy, 0), dx, 1); acc += np.roll(np.roll(rgb, dy, 0), dx, 1) * k[..., None]; cnt += k
        new = (~known) & (cnt > 0); rgb[new] = acc[new] / cnt[new][:, None]; known |= new
    if (~known).any(): rgb[~known] = rgb[known].mean(0)
    return rgb
S = CELL_PX * 2; D = np.zeros((S, S, 4)); N = np.zeros((S, S, 3))
for c in range(4):
    col = np.asarray(Image.open(os.path.join(tmp, f"col{c}.png")).convert("RGBA")) / 255.0
    nor = np.asarray(Image.open(os.path.join(tmp, f"nor{c}.png")).convert("RGBA")) / 255.0
    x0 = (c % 2) * CELL_PX; y0 = (1 - c // 2) * CELL_PX
    D[y0:y0 + CELL_PX, x0:x0 + CELL_PX, :3] = bleed(col[..., :3], col[..., 3]); D[y0:y0 + CELL_PX, x0:x0 + CELL_PX, 3] = col[..., 3]
    nb = bleed(nor[..., :3], nor[..., 3]) * 0.5 + np.array((0.5, 0.5, 1.0)) * 0.5   # abgeflacht, Halme sind dünn
    N[y0:y0 + CELL_PX, x0:x0 + CELL_PX] = nb
Image.fromarray((D * 255 + 0.5).astype(np.uint8), "RGBA").save(os.path.join(OUT, "T_LeafCrop_D.png"))
Image.fromarray((N * 255 + 0.5).astype(np.uint8), "RGB").save(os.path.join(OUT, "T_LeafCrop_N.png"))
ms = np.zeros((S, S, 4), np.uint8); ms[..., 3] = 90; Image.fromarray(ms, "RGBA").save(os.path.join(OUT, "T_LeafCrop_MS.png"))
print("CROP ok")
