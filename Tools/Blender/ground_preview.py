# Vorschau: Bodenschichten im Maßstab des Spiels (Kachel 4 m) mit den Frühzeit-Gebäuden aus den FBX-Dateien.
# Aufruf: blender -b -noaudio --python ground_preview.py -- <texdir> <fbxdir mit Unterordner Textures> <out.png> <samples> <kamera: rts|nah>
import bpy, sys, math, os, random, bmesh
from mathutils import Vector, noise
a = sys.argv[sys.argv.index("--") + 1:]
TEX, FBX, OUT, SAMPLES, CAM = a[0], a[1], a[2], int(a[3]), a[4]
bpy.ops.wm.read_factory_settings(use_empty=True); sc = bpy.context.scene
REPEAT = {"Grass": 5.0, "Earth": 4.0, "Path": 4.5, "Field": 6.0, "Forest": 4.6}

# Wie im Spiel: ein Bodenmaterial mischt die Schichten per Maske (Splat-Map) weich ineinander.
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
RES, SIZE = 2048, 160.0
def to_px(x, y): return ((x / SIZE + 0.5) * RES, (0.5 - y / SIZE) * RES)
rng = np.random.default_rng(7)
def fbm():
    n = np.zeros((RES, RES))
    for k, amp in ((8, 1.0), (32, 0.5), (128, 0.25)):
        g = rng.random((k, k)); n += amp * np.asarray(Image.fromarray((g * 255).astype(np.uint8)).resize((RES, RES), Image.BICUBIC)) / 255
    return n / 1.75 - 0.5
def mask(poly, soft, rough):
    im = Image.new("L", (RES, RES), 0); ImageDraw.Draw(im).polygon([to_px(*q) for q in poly], fill=255)
    m = np.asarray(im.filter(ImageFilter.GaussianBlur(soft))) / 255.0
    return np.clip((m - 0.5 + fbm() * rough) * 3.0 + 0.5, 0, 1)
path = []
for k in range(26):
    t = k / 25; path.append((-40 + 80 * t, -2 + 3.5 * math.sin(t * 3.0)))
strip = [(x, y - 1.1) for x, y in path] + [(x, y + 1.1) for x, y in reversed(path)]
LAYERS = [("Forest", [(-40, 14), (-8, 12), (-4, 40), (-40, 40)], 30, 0.9),
          ("Field", [(6, 6), (26, 5), (27, 20), (7, 22)], 6, 0.35),
          ("Earth", [(-13, -9), (5, -10), (6, 4), (-12, 5)], 18, 0.7),
          ("Path", strip, 6, 0.5)]
MASKS = {}
for name, poly, soft, rough in LAYERS:
    fn = os.path.join(os.path.dirname(OUT), f"mask_{name}.png"); Image.fromarray((mask(poly, soft, rough) * 255).astype(np.uint8)).save(fn); MASKS[name] = fn

m = bpy.data.materials.new("Ground"); m.use_nodes = True; nt = m.node_tree; bsdf = nt.nodes["Principled BSDF"]
tc = nt.nodes.new("ShaderNodeTexCoord")
def tex(path, vec, nc):
    n = nt.nodes.new("ShaderNodeTexImage"); n.image = bpy.data.images.load(path)
    if nc: n.image.colorspace_settings.name = "Non-Color"
    nt.links.new(vec, n.inputs["Vector"]); return n
def scaled(rep):
    mp = nt.nodes.new("ShaderNodeMapping"); mp.inputs["Scale"].default_value = (1 / rep, 1 / rep, 1); nt.links.new(tc.outputs["Object"], mp.inputs["Vector"]); return mp.outputs["Vector"]
def layer(name):
    v = scaled(REPEAT[name]); return tex(os.path.join(TEX, f"T_Ground_{name}_D.jpg"), v, False).outputs["Color"], tex(os.path.join(TEX, f"T_Ground_{name}_N.jpg"), v, True).outputs["Color"]
def mix(a, b, f):
    x = nt.nodes.new("ShaderNodeMix"); x.data_type = "RGBA"; nt.links.new(f, x.inputs["Factor"]); nt.links.new(a, x.inputs[6]); nt.links.new(b, x.inputs[7]); return x.outputs[2]
col, nor = layer("Grass")
for name, *_ in LAYERS:
    f = tex(MASKS[name], tc.outputs["UV"], True).outputs["Color"]; c, n = layer(name); col = mix(col, c, f); nor = mix(nor, n, f)
mt = tex(os.path.join(TEX, "T_Ground_Macro.png"), scaled(64), True)  # Makro-Variation gegen sichtbare Kachelung
sep = nt.nodes.new("ShaderNodeSeparateColor"); nt.links.new(mt.outputs["Color"], sep.inputs["Color"])
mr = nt.nodes.new("ShaderNodeMapRange"); mr.inputs["To Min"].default_value = 0.8; mr.inputs["To Max"].default_value = 1.15; nt.links.new(sep.outputs[0], mr.inputs["Value"])
mul = nt.nodes.new("ShaderNodeMix"); mul.data_type = "RGBA"; mul.blend_type = "MULTIPLY"; mul.inputs["Factor"].default_value = 1.0
nt.links.new(col, mul.inputs[6]); nt.links.new(mr.outputs["Result"], mul.inputs[7]); nt.links.new(mul.outputs[2], bsdf.inputs["Base Color"])
nm = nt.nodes.new("ShaderNodeNormalMap"); nt.links.new(nor, nm.inputs["Color"]); nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"]); bsdf.inputs["Roughness"].default_value = 0.9
bpy.ops.mesh.primitive_plane_add(size=SIZE); g = bpy.context.object; g.data.materials.append(m)

def load(name, loc, rot):
    bpy.ops.import_scene.fbx(filepath=os.path.join(FBX, name + ".fbx"), use_image_search=True)
    o = bpy.context.selected_objects[0]; o.location = loc; o.rotation_euler[2] = math.radians(rot)
    for m in o.data.materials:  # FBX ist mit path_mode STRIP exportiert: Bilder aus dem Textures-Ordner neu laden
        for n in m.node_tree.nodes:
            if n.type == "TEX_IMAGE" and n.image: n.image.filepath = os.path.join(FBX, "Textures", os.path.basename(n.image.filepath)); n.image.reload()
load("WB_Prim_House_Wattle", (-4, -4.5, 0), 0)
load("WB_Prim_Granary", (3.2, 0.2, 0), 180)
load("WB_Prim_Tent_Hide", (-12.5, 2.0, 0), 20)
load("WB_Prim_House_Wattle", (16, -10, 0), 95)
load("WB_Prim_Tent_Hide", (-17, -7, 0), -30)

sun = bpy.data.lights.new("Sun", "SUN"); sun.energy = 3.4; sun.angle = math.radians(2.5)
so = bpy.data.objects.new("Sun", sun); sc.collection.objects.link(so); so.rotation_euler = (math.radians(50), 0, math.radians(-35))
w = bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True
w.node_tree.nodes["Background"].inputs["Color"].default_value = (0.62, 0.72, 0.88, 1); w.node_tree.nodes["Background"].inputs["Strength"].default_value = 1.0
cam = bpy.data.cameras.new("C"); co = bpy.data.objects.new("C", cam); sc.collection.objects.link(co); sc.camera = co
if CAM == "rts":  # Spielkamera: ca. 45 m Abstand, 45° Blick
    cam.lens = 35; co.location = (8, -46, 40); tgt = Vector((2, 2, 0))
else:
    cam.lens = 32; co.location = (9, -21, 7.5); tgt = Vector((-2, -1, 1.5))
co.rotation_euler = (tgt - co.location).to_track_quat("-Z", "Y").to_euler()
sc.render.engine = "CYCLES"; sc.cycles.samples = SAMPLES; sc.cycles.device = "CPU"; sc.cycles.use_denoising = False
sc.render.resolution_x = 1600; sc.render.resolution_y = 900; sc.view_settings.view_transform = "AgX"; sc.view_settings.look = "AgX - Medium High Contrast"
sc.render.filepath = OUT; bpy.ops.render.render(write_still=True)
