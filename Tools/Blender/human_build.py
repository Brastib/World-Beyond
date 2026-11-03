# World Beyond: Frühzeit-Menschen aus dem MakeHuman-Grundkörper (CC0) mit eigener Fellkleidung. Blender 4.0, headless.
# Liest nur Daten (base.obj, Targets, Proxy-/mhclo-Dateien, Hauttexturen); eigener Code, kein fremdes Add-on.
# Aufruf: blender -b -noaudio --python human_build.py -- <mhdata> <sysassets> <texdir> <outdir> <male|female> [seed]
import bpy, bmesh, sys, os, gzip, json, math, random
import numpy as np
from mathutils import Vector

a = sys.argv[sys.argv.index("--") + 1:]
MHD, SYS, TEX, OUT, SEX = a[0], a[1], a[2], a[3], a[4]; SEED = int(a[5]) if len(a) > 5 else 1
rng = random.Random(SEED * 10 + (1 if SEX == "male" else 2))
HEIGHT = 1.80 if SEX == "male" else 1.68
NAME = "WB_Human_Prim_" + ("Male" if SEX == "male" else "Female")
bpy.ops.wm.read_factory_settings(use_empty=True); sc = bpy.context.scene

# ---------- Grundkörper + Targets (MakeHuman-Raum: Dezimeter, Y oben, +Z vorn) ----------
base = np.array([[float(t) for t in l.split()[1:4]] for l in open(os.path.join(MHD, "3dobjs/base.obj")) if l.startswith("v ")])
def target(rel, w):
    if w == 0: return
    with gzip.open(os.path.join(MHD, "targets", rel + ".target.gz"), "rt") as f:
        for l in f:
            p = l.split()
            if len(p) == 4 and not l.startswith("#"): base[int(p[0])] += w * np.array([float(p[1]), float(p[2]), float(p[3])])
# Herkunft gemischt (eigene Welt): Anteile frei gewählt; Muskulös durch Alltagsarbeit
races = {"caucasian": 0.45, "african": 0.25, "asian": 0.30}
for r, w in races.items(): target(f"macrodetails/{r}-{SEX}-young", w)
target(f"macrodetails/universal-{SEX}-young-maxmuscle-averageweight", 0.35)
target(f"macrodetails/universal-{SEX}-young-averagemuscle-minweight", 0.25)

groups = json.load(open(os.path.join(MHD, "mesh_metadata/basemesh_vertex_groups.json")))
def gverts(name): return [i for s, e in groups[name] for i in range(s, e + 1)]
def joint(name): return base[gverts("joint-" + name)].mean(0)

def to_bl(p):  # MakeHuman -> Blender (Meter, Z oben, Figur schaut nach -Y)
    p = np.asarray(p); return np.stack([p[..., 0], -p[..., 2], p[..., 1]], -1) * 0.1

def read_obj(path):
    vs, vts, faces = [], [], []
    for l in open(path):
        if l.startswith("v "): vs.append([float(t) for t in l.split()[1:4]])
        elif l.startswith("vt "): vts.append([float(t) for t in l.split()[1:3]])
        elif l.startswith("f "):
            fv, ft = [], []
            for t in l.split()[1:]:
                q = t.split("/"); fv.append(int(q[0]) - 1); ft.append(int(q[1]) - 1 if len(q) > 1 and q[1] else None)
            faces.append((fv, ft))
    return np.array(vs), vts, faces

def fit(path):
    """Proxy/mhclo an den verformten Grundkörper anpassen (gewichtete Dreiecke + skalierter Versatz)."""
    lines = open(path).read().splitlines(); sc_ = {}; rows = []; inv = False; objf = None; mat = None
    for l in lines:
        p = l.split()
        if not p or p[0].startswith("#"): continue
        if p[0] in ("x_scale", "y_scale", "z_scale"): sc_[p[0][0]] = (int(p[1]), int(p[2]), float(p[3])); continue
        if p[0] == "obj_file": objf = p[1]; continue
        if p[0] == "material": mat = p[1]; continue
        if p[0] == "verts": inv = True; continue
        if inv:
            try: rows.append([float(t) for t in p])
            except ValueError: inv = False
    s = np.ones(3)
    for k, ax in (("x", 0), ("y", 1), ("z", 2)):
        if k in sc_:
            i, j, ref = sc_[k]; s[ax] = abs(base[i][ax] - base[j][ax]) / ref
    out = []
    for r in rows:
        if len(r) == 1: out.append(base[int(r[0])].copy())
        else:
            i = [int(r[0]), int(r[1]), int(r[2])]; w = r[3:6]; d = np.array(r[6:9])
            out.append(w[0] * base[i[0]] + w[1] * base[i[1]] + w[2] * base[i[2]] + d * s)
    return np.array(out), os.path.join(os.path.dirname(path), objf), mat

def mesh_obj(name, verts_bl, obj_path, material):
    _, vts, faces = read_obj(obj_path)
    me = bpy.data.meshes.new(name); bm = bmesh.new(); uv = bm.loops.layers.uv.new("UVMap")
    bv = [bm.verts.new(v) for v in verts_bl]
    for fv, ft in faces:
        try: f = bm.faces.new([bv[i] for i in fv])
        except ValueError: continue
        f.smooth = True
        for lp, ti in zip(f.loops, ft):
            if ti is not None: lp[uv].uv = vts[ti]
    bm.to_mesh(me); bm.free(); me.materials.append(material)
    o = bpy.data.objects.new(name, me); sc.collection.objects.link(o); return o

def image_mat(name, img, rough=0.6, alpha=False, sss=0.0, normal=None, tint=None):
    m = bpy.data.materials.new(name); m.use_nodes = True; nt = m.node_tree; b = nt.nodes["Principled BSDF"]
    t = nt.nodes.new("ShaderNodeTexImage"); t.image = bpy.data.images.load(img); nt.links.new(t.outputs["Color"], b.inputs["Base Color"])
    if tint:  # nur Vorschau; im Spiel als Materialfarbe (Base Color Tint) einstellbar
        mx = nt.nodes.new("ShaderNodeMix"); mx.data_type = "RGBA"; mx.blend_type = "MULTIPLY"; mx.inputs["Factor"].default_value = 1.0
        mx.inputs[7].default_value = tint + (1,); nt.links.new(t.outputs["Color"], mx.inputs[6]); nt.links.new(mx.outputs[2], b.inputs["Base Color"])
    b.inputs["Roughness"].default_value = rough
    if sss:
        b.inputs["Subsurface Weight"].default_value = sss; b.inputs["Subsurface Radius"].default_value = (0.9, 0.35, 0.2)
        b.inputs["Subsurface Scale"].default_value = 0.01
    if alpha: nt.links.new(t.outputs["Alpha"], b.inputs["Alpha"]); m.blend_method = "CLIP"
    if normal:
        n = nt.nodes.new("ShaderNodeTexImage"); n.image = bpy.data.images.load(normal); n.image.colorspace_settings.name = "Non-Color"
        nm = nt.nodes.new("ShaderNodeNormalMap"); nt.links.new(n.outputs["Color"], nm.inputs["Color"]); nt.links.new(nm.outputs["Normal"], b.inputs["Normal"])
    return m

def mhmat_texture(mhmat_path):
    for l in open(mhmat_path):
        if l.startswith("diffuseTexture"): return os.path.join(os.path.dirname(mhmat_path), l.split()[1])

# ---------- Körper (Proxy-Topologie mit ca. 13k Dreiecken) ----------
skin_dir = os.path.join(SYS, "skins", f"young_caucasian_{SEX}")
skin_img = [f for f in os.listdir(skin_dir) if f.endswith("_diffuse.png")][0]
M_SKIN = image_mat("M_Skin", os.path.join(skin_dir, skin_img), 0.5, sss=0.15, tint=(0.78, 0.62, 0.5))  # sonnengebräunt
pv, pobj, _ = fit(os.path.join(SYS, "proxymeshes", f"{SEX}_generic", f"{SEX}_generic.proxy"))
body = mesh_obj("Body", to_bl(pv), pobj, M_SKIN)

parts = [body]
def add_asset(kind, name, alpha=True, rough=0.6):
    d = os.path.join(SYS, kind, name); mh = [f for f in os.listdir(d) if f.endswith(".mhclo")][0]
    v, objp, matf = fit(os.path.join(d, mh))
    tex = mhmat_texture(os.path.join(d, matf)) if matf else None
    m = image_mat("M_" + name, tex, rough, alpha=alpha); o = mesh_obj(name, to_bl(v), objp, m); parts.append(o); return o
eyes = add_asset("eyes", "low-poly", alpha=False, rough=0.2)
eyes.data.materials[0] = image_mat("M_Eyes", os.path.join(SYS, "eyes/materials/brown_eye.png"), 0.15)
add_asset("eyebrows", "eyebrow001" if SEX == "male" else "eyebrow002")
add_asset("hair", "short02" if SEX == "male" else "braid01", rough=0.55)

# Gelenkpunkte in Blender-Koordinaten
J = {k: Vector(to_bl(joint(k))) for k in ("pelvis", "neck", "head", "l-upper-leg", "r-upper-leg", "l-knee", "r-knee", "l-ankle", "r-ankle",
                                         "l-shoulder", "r-shoulder", "l-elbow", "r-elbow", "spine-3", "l-toe-3-1", "r-toe-3-1")}

# ---------- Eigene Fellkleidung (Leder T_Hide, Fellbesatz T_Fur) ----------
M_HIDE = image_mat("M_Hide", os.path.join(TEX, "T_Hide_D.jpg"), 0.75, normal=os.path.join(TEX, "T_Hide_N.jpg"))
M_FUR = image_mat("M_Fur", os.path.join(TEX, "T_Fur_D.jpg"), 0.9, normal=os.path.join(TEX, "T_Fur_N.jpg"))
M_CORD = image_mat("M_Cord", os.path.join(TEX, "T_Hide_D.jpg"), 0.8, tint=(0.45, 0.36, 0.28))  # dunkler Lederriemen

def seg_dist(p, a_, b_):
    ab = b_ - a_; t = max(0.0, min(1.0, (p - a_).dot(ab) / ab.length_squared)); return (p - (a_ + ab * t)).length, t
SEGS = {"spine": (J["pelvis"], J["neck"]), "uarm_l": (J["l-shoulder"], J["l-elbow"]), "uarm_r": (J["r-shoulder"], J["r-elbow"]),
        "thigh_l": (J["l-upper-leg"], J["l-knee"]), "thigh_r": (J["r-upper-leg"], J["r-knee"]),
        "calf_l": (J["l-knee"], J["l-ankle"]), "calf_r": (J["r-knee"], J["r-ankle"]),
        "foot_l": (J["l-ankle"], J["l-toe-3-1"]), "foot_r": (J["r-ankle"], J["r-toe-3-1"]), "head": (J["neck"], J["head"])}
def region(p):
    best = None
    for k, (s0, s1) in SEGS.items():
        d, t = seg_dist(p, s0, s1)
        if best is None or d < best[1]: best = (k, d, t)
    return best

def cyl_uv(bm, uv, center, tile=0.9):
    for f in bm.faces:
        for lp in f.loops:
            c = lp.vert.co; ang = math.atan2(c.y - center.y, c.x - center.x)
            lp[uv].uv = (ang * 0.25 / tile, c.z / tile)

def shell(name, pick, offset, mat, hem=None):
    """Kleidungsstück als nach außen versetzte Kopie der gewählten Körperflächen (folgt später denselben Knochen)."""
    bm = bmesh.new(); bm.from_mesh(body.data); bm.verts.ensure_lookup_table(); bm.normal_update()
    keep = {v.index: pick(v.co.copy()) for v in bm.verts}
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if not all(keep[v.index] for v in f.verts)], context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    for v in bm.verts:
        k = offset * (1 + 0.25 * math.sin(v.co.z * 40 + v.co.x * 25))
        v.co += v.normal * k
    for lay in list(bm.loops.layers.uv): bm.loops.layers.uv.remove(lay)
    uv = bm.loops.layers.uv.new("UVMap"); cyl_uv(bm, uv, J["pelvis"])
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free(); me.materials.append(mat)
    o = bpy.data.objects.new(name, me); sc.collection.objects.link(o)
    m = o.modifiers.new("dick", "SOLIDIFY"); m.thickness = 0.004; m.offset = 1
    bpy.context.view_layer.objects.active = o; bpy.ops.object.modifier_apply(modifier=m.name)
    for p in o.data.polygons: p.use_smooth = True
    parts.append(o); return o

hip_z = J["pelvis"].z; neck_z = J["neck"].z
def tunic_pick(p):
    k, d, t = region(p)
    if k == "spine": return p.z < neck_z - 0.04
    if k.startswith("uarm"): return t < 0.42
    return False
shell("Tunic", tunic_pick, 0.012, M_HIDE)
def legwrap_pick(p):
    k, d, t = region(p); return (k.startswith("calf") and t > 0.25) or k.startswith("foot")
shell("Wraps", legwrap_pick, 0.008, M_HIDE)

def ring_tube(z_top, z_bot, n_ang=40, n_z=8, flare=0.06, mat=M_HIDE, jag=0.05, name="Skirt", pad=0.03):
    """Rock: Röhre um die Hüfte, Radien aus dem Körperquerschnitt, unten ausgefranster Saum."""
    co = np.array([v.co[:] for v in body.data.vertices])
    rows = []; bm = bmesh.new(); uv = bm.loops.layers.uv.new("UVMap")
    c = Vector((J["pelvis"].x, J["pelvis"].y, 0))
    for j in range(n_z + 1):
        z = z_top + (z_bot - z_top) * j / n_z
        sl = co[(np.abs(co[:, 2] - z) < 0.02) & (np.hypot(co[:, 0] - c.x, co[:, 1] - c.y) < 0.24)]  # ohne Arme/Hände
        rr = []
        for i in range(n_ang):
            ang = 2 * math.pi * i / n_ang; dvec = np.array([math.cos(ang), math.sin(ang)])
            proj = (sl[:, :2] - np.array([c.x, c.y])) @ dvec if len(sl) else np.array([0.15])
            r = max(proj.max(), 0.1) + pad + flare * j / n_z
            if j == n_z: r += rng.uniform(0, 0.015)
            zz = z - (rng.uniform(0, jag) if j == n_z else 0)
            rr.append(bm.verts.new((c.x + math.cos(ang) * r, c.y + math.sin(ang) * r, zz)))
        rows.append(rr)
    for j in range(n_z):
        for i in range(n_ang):
            i2 = (i + 1) % n_ang
            f = bm.faces.new((rows[j][i], rows[j][i2], rows[j + 1][i2], rows[j + 1][i])); f.smooth = True
    cyl_uv(bm, uv, c); bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free(); me.materials.append(mat)
    o = bpy.data.objects.new(name, me); sc.collection.objects.link(o)
    m = o.modifiers.new("dick", "SOLIDIFY"); m.thickness = 0.005
    bpy.context.view_layer.objects.active = o; bpy.ops.object.modifier_apply(modifier=m.name)
    parts.append(o); return o

knee_z = (J["l-knee"].z + J["r-knee"].z) / 2
skirt_bot = knee_z + (hip_z - knee_z) * (0.35 if SEX == "male" else 0.05)
ring_tube(hip_z + 0.07, skirt_bot, flare=0.05 if SEX == "male" else 0.09, jag=0.06)
ring_tube(hip_z + 0.11, hip_z + 0.06, n_z=1, flare=0.0, mat=M_CORD, jag=0.0, name="Belt", pad=0.045)
# Fellumhang über den Schultern
def cape_pick(p):
    k, d, t = region(p)
    return (k == "spine" and p.z > neck_z - 0.2 and p.z < neck_z + 0.02) or (k.startswith("uarm") and t < 0.18 and p.z > neck_z - 0.22)
shell("Cape", cape_pick, 0.03, M_FUR)

# ---------- Zusammenführen, Größe 1,80 / 1,68 m, Füße auf dem Boden ----------
mats_order = []
for o in parts:
    for m in o.data.materials:
        if m and m.name not in [x.name for x in mats_order]: mats_order.append(m)
for o in parts:
    own = [m.name for m in o.data.materials]; idx = [next(i for i, x in enumerate(mats_order) if x.name == n) for n in own]
    old = [p.material_index for p in o.data.polygons]
    o.data.materials.clear()   # setzt in Blender 4 die Indizes zurück, deshalb danach neu setzen
    for m in mats_order: o.data.materials.append(m)
    for p, oi in zip(o.data.polygons, old): p.material_index = idx[oi]
    print("PART", o.name, len(o.data.polygons))
    for lay in list(o.data.uv_layers):
        if lay.name != "UVMap": o.data.uv_layers.remove(lay)
bpy.ops.object.select_all(action="DESELECT")
for o in parts: o.select_set(True)
bpy.context.view_layer.objects.active = body; bpy.ops.object.join()
obj = bpy.context.object; obj.name = NAME; obj.data.name = NAME
co = np.array([v.co[:] for v in obj.data.vertices]); zmin = co[:, 2].min(); zmax = co[:, 2].max()
k = HEIGHT / (zmax - zmin)
for v in obj.data.vertices: v.co = Vector(((v.co.x) * k, (v.co.y) * k, (v.co.z - zmin) * k))
obj.data.update(); obj.data.calc_loop_triangles()
co = np.array([v.co[:] for v in obj.data.vertices])
print(f"STATS {NAME} tris={len(obj.data.loop_triangles)} size_m=({np.ptp(co[:, 0]):.2f}, {np.ptp(co[:, 1]):.2f}, {co[:, 2].max():.2f}) mats={[m.name for m in obj.data.materials]}")
os.makedirs(OUT, exist_ok=True)
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, NAME + ".fbx"), use_selection=True, object_types={"MESH"},
                         apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                         mesh_smooth_type="FACE", path_mode="STRIP", add_leaf_bones=False)

# ---------- optionale Vorschau: <preview.png> <samples> als 7./8. Argument ----------
if len(a) > 6:
    PREV, SMP = a[6], int(a[7]) if len(a) > 7 else 32
    for m in obj.data.materials:
        if m.name in ("M_short02", "M_braid01", "M_eyebrow001", "M_eyebrow002"): m.blend_method = "CLIP"
    hut = os.path.join(os.path.dirname(os.path.abspath(TEX)), "..", "prim", "out2", "WB_Prim_Hut_Wattle.fbx")
    if os.path.exists(hut):
        bpy.ops.import_scene.fbx(filepath=hut); h = bpy.context.selected_objects[0]; h.location = (2.6, 2.2, 0); h.rotation_euler[2] = math.radians(-20)
        for mm in h.data.materials:
            for n in mm.node_tree.nodes:
                if n.type == "TEX_IMAGE" and n.image:
                    n.image.filepath = os.path.join(os.path.dirname(hut), "..", "tex", os.path.basename(n.image.filepath)); n.image.reload()
    g = image_mat("G", "/home/user/World-Beyond/Assets/_Project/Art/Terrain/Resources/WorldBeyondTerrain/T_Ground_Earth_D.jpg", 0.9)
    bpy.ops.mesh.primitive_plane_add(size=40); gp = bpy.context.object; gp.data.materials.append(g)
    bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.uv.cube_project(cube_size=4); bpy.ops.object.mode_set(mode="OBJECT")
    sun = bpy.data.lights.new("S", "SUN"); sun.energy = 3.2; sun.angle = math.radians(3)
    so = bpy.data.objects.new("S", sun); sc.collection.objects.link(so); so.rotation_euler = (math.radians(50), 0, math.radians(-30))
    w = bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True; w.node_tree.nodes["Background"].inputs["Color"].default_value = (0.62, 0.72, 0.88, 1)
    cam = bpy.data.cameras.new("C"); cam.lens = 50; co_ = bpy.data.objects.new("C", cam); sc.collection.objects.link(co_); sc.camera = co_
    co_.location = (1.6, -4.6, 1.55); tgt = Vector((0.5, 0.4, 1.1)); co_.rotation_euler = (tgt - co_.location).to_track_quat("-Z", "Y").to_euler()
    sc.render.engine = "CYCLES"; sc.cycles.samples = SMP; sc.cycles.device = "CPU"; sc.cycles.use_denoising = False
    sc.render.resolution_x = 1200; sc.render.resolution_y = 900; sc.view_settings.view_transform = "AgX"; sc.view_settings.look = "AgX - Medium High Contrast"
    sc.render.filepath = PREV; bpy.ops.render.render(write_still=True)
