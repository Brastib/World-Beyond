# Vorschau: Bäume + Hütte auf Wiesenboden. Aufruf: blender -b -noaudio --python tree_preview.py -- <treedir> <texdir> <hutfbx-dir> <groundtex> <out.png> <samples> <rts|nah>
# Aufruf: blender -b -noaudio --python tree_preview.py -- <treedir> <texdir> <hut-fbx-ordner mit Textures> <bodentex> <out.png> <samples> <rts|nah>
from mathutils import Vector
a = sys.argv[sys.argv.index("--") + 1:]
TD, TEX, HUT, GT, OUT, SAMPLES, CAM = a[0], a[1], a[2], a[3], a[4], int(a[5]), a[6]
bpy.ops.wm.read_factory_settings(use_empty=True); sc = bpy.context.scene
def fix_images(o, texdirs, swap=None):
    for m in o.data.materials:
        for n in m.node_tree.nodes:
            if n.type == "TEX_IMAGE" and n.image:
                b = os.path.basename(n.image.filepath)
                if swap and b.startswith(swap[0]): b = b.replace(swap[0], swap[1])
                for d in texdirs:
                    if os.path.exists(os.path.join(d, b)): n.image = bpy.data.images.load(os.path.join(d, b), check_existing=True); break
        # Alpha der Blatttextur anschließen (FBX-Import verbindet sie nicht immer)
        if "Leaf" in m.name:
            nt = m.node_tree; bs = [n for n in nt.nodes if n.type == "BSDF_PRINCIPLED"][0]
            img = [n for n in nt.nodes if n.type == "TEX_IMAGE" and "_D" in n.image.name][0]
            nt.links.new(img.outputs["Alpha"], bs.inputs["Alpha"])
def load(path, loc, rot, texdirs, swap=None, scale=1.0):
    bpy.ops.import_scene.fbx(filepath=path); o = bpy.context.selected_objects[0]
    if swap:
        o.data = o.data.copy()
        for i, m in enumerate(o.data.materials): o.data.materials[i] = m.copy()
    o.location = loc; o.rotation_euler[2] = math.radians(rot); o.scale = (scale,) * 3; fix_images(o, texdirs, swap); return o
T = [TEX]
load(os.path.join(TD, "WB_Tree_Broadleaf.fbx"), (-9, 6, 0), 0, T)
load(os.path.join(TD, "WB_Tree_Spruce.fbx"), (-1, 11, 0), 30, T)
load(os.path.join(TD, "WB_Tree_Cherry.fbx"), (7, 4, 0), 0, T)
load(os.path.join(TD, "WB_Tree_Cherry.fbx"), (14, 12, 0), 140, T, swap=("T_LeafCherry_", "T_LeafCherryBlossom_"))
load(os.path.join(TD, "WB_Tree_Spruce.fbx"), (-15, 15, 0), 200, T, scale=0.8)
load(os.path.join(HUT, "WB_Prim_Hut_Wattle.fbx"), (0, -1, 0), 10, [os.path.join(HUT, "Textures")])
# Boden
m = bpy.data.materials.new("G"); m.use_nodes = True; nt = m.node_tree; b = nt.nodes["Principled BSDF"]
tc = nt.nodes.new("ShaderNodeTexCoord"); mp = nt.nodes.new("ShaderNodeMapping"); mp.inputs["Scale"].default_value = (0.2, 0.2, 0.2)
nt.links.new(tc.outputs["Object"], mp.inputs["Vector"])
for suf, inp in (("D", "Base Color"), ("N", None)):
    n = nt.nodes.new("ShaderNodeTexImage"); n.image = bpy.data.images.load(os.path.join(GT, f"T_Ground_Grass_{suf}.jpg")); nt.links.new(mp.outputs["Vector"], n.inputs["Vector"])
    if inp: nt.links.new(n.outputs["Color"], b.inputs[inp])
    else:
        n.image.colorspace_settings.name = "Non-Color"; nm = nt.nodes.new("ShaderNodeNormalMap"); nt.links.new(n.outputs["Color"], nm.inputs["Color"]); nt.links.new(nm.outputs["Normal"], b.inputs["Normal"])
bpy.ops.mesh.primitive_plane_add(size=200); bpy.context.object.data.materials.append(m)
sun = bpy.data.lights.new("S", "SUN"); sun.energy = 3.4; sun.angle = math.radians(2.5)
so = bpy.data.objects.new("S", sun); sc.collection.objects.link(so); so.rotation_euler = (math.radians(50), 0, math.radians(-35))
w = bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True
w.node_tree.nodes["Background"].inputs["Color"].default_value = (0.62, 0.72, 0.88, 1)
cam = bpy.data.cameras.new("C"); co = bpy.data.objects.new("C", cam); sc.collection.objects.link(co); sc.camera = co
if CAM == "rts": cam.lens = 35; co.location = (2, -40, 36); tgt = Vector((0, 6, 0))
else: cam.lens = 26; co.location = (4, -18, 4.5); tgt = Vector((2.5, 6, 4.0))
co.rotation_euler = (tgt - co.location).to_track_quat("-Z", "Y").to_euler()
sc.render.engine = "CYCLES"; sc.cycles.samples = SAMPLES; sc.cycles.device = "CPU"; sc.cycles.use_denoising = False
sc.cycles.transparent_max_bounces = 64; sc.cycles.max_bounces = 6
sc.render.resolution_x = 1600; sc.render.resolution_y = 900; sc.view_settings.view_transform = "AgX"; sc.view_settings.look = "AgX - Medium High Contrast"
sc.render.filepath = OUT; bpy.ops.render.render(write_still=True)
