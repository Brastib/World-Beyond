# World Beyond: Frühzeit-Gebäude für Unity aus Blender (headless).
# Aufruf: blender -b -noaudio --python prim_buildings.py -- <house|tent|granary> <texdir> <outdir> [preview.png] [samples]
# Ergebnis: <outdir>/WB_Prim_*.fbx (Ursprung = Bodenmitte, 1 Einheit = 1 m, Y oben in Unity), optional Vorschaubild.
import bpy, bmesh, sys, math, random, os
from mathutils import Vector, noise

argv = sys.argv[sys.argv.index("--") + 1:]
MODEL, TEX, OUTDIR = argv[0], argv[1], argv[2]
PREVIEW = argv[3] if len(argv) > 3 else None
SAMPLES = int(argv[4]) if len(argv) > 4 else 64
NAMES = {"house": "WB_Prim_House_Wattle", "tent": "WB_Prim_Tent_Hide", "granary": "WB_Prim_Granary"}
rng = random.Random({"house": 1, "tent": 2, "granary": 3}[MODEL])
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene

# ---------- Materialien (Texturen CC0, Poly Haven) ----------
TEXSETS = {  # Materialname: (Datei-Präfix, Kachelgröße in m)
    "M_Thatch": ("T_Thatch", 2.5), "M_Daub": ("T_Daub", 1.6), "M_Bark": ("T_Bark", 1.0),
    "M_Wood": ("T_Wood", 0.5), "M_Hide": ("T_Hide", 0.9), "M_Earth": ("T_Earth", 1.3),
}
MATS = {}
def material(name):
    if name in MATS: return MATS[name]
    pre, _ = TEXSETS[name]
    m = bpy.data.materials.new(name); m.use_nodes = True; nt = m.node_tree; b = nt.nodes["Principled BSDF"]
    def img(suffix, non_color):
        n = nt.nodes.new("ShaderNodeTexImage"); n.image = bpy.data.images.load(os.path.join(TEX, f"{pre}_{suffix}.jpg"))
        if non_color: n.image.colorspace_settings.name = "Non-Color"
        return n
    d = img("D", False); nt.links.new(d.outputs["Color"], b.inputs["Base Color"])
    r = img("R", True); nt.links.new(r.outputs["Color"], b.inputs["Roughness"])
    n = img("N", True); nm = nt.nodes.new("ShaderNodeNormalMap"); nt.links.new(n.outputs["Color"], nm.inputs["Color"])
    nt.links.new(nm.outputs["Normal"], b.inputs["Normal"])
    MATS[name] = m; return m

PARTS = []  # (bmesh-Daten werden pro Teil als Objekt angelegt und am Ende verbunden)
def finish(bm, mat):
    me = bpy.data.meshes.new(mat); bm.normal_update(); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(mat, me); scene.collection.objects.link(o)
    o.data.materials.append(material(mat)); PARTS.append(o); return o

def tube(path, radii, sides, mat, tile=None, cap=True, jitter=0.0, uoff=None):
    """Rohr entlang Punkten (Stämme, Ruten, Stangen). UV zylindrisch in Weltmaß."""
    tile = tile or TEXSETS[mat][1]; uoff = rng.random() if uoff is None else uoff
    bm = bmesh.new(); uv = bm.loops.layers.uv.new("UVMap")
    pts = [Vector(p) for p in path]; rings = []; dist = 0.0
    for i, p in enumerate(pts):
        t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        a = t.orthogonal().normalized(); b2 = t.cross(a).normalized()
        if i > 0: dist += (p - pts[i - 1]).length
        ring = []
        for k in range(sides):
            ang = 2 * math.pi * k / sides
            rr = radii[i] * (1 + jitter * noise.noise(Vector((p.x * 3 + k, p.y * 3, p.z * 3))))
            ring.append((bm.verts.new(p + (a * math.cos(ang) + b2 * math.sin(ang)) * rr), dist))
        rings.append(ring)
    circ = 2 * math.pi * max(radii)
    for i in range(len(rings) - 1):
        for k in range(sides):
            k2 = (k + 1) % sides
            f = bm.faces.new((rings[i][k][0], rings[i][k2][0], rings[i + 1][k2][0], rings[i + 1][k][0]))
            us = [k / sides, (k + 1) / sides, (k + 1) / sides, k / sides]; vs = [rings[i][0][1], rings[i][0][1], rings[i + 1][0][1], rings[i + 1][0][1]]
            for lp, uu, vv in zip(f.loops, us, vs):
                lp[uv].uv = (uoff + uu * circ / tile, vv / tile)
    if cap:
        for ring in (rings[0], rings[-1]):
            f = bm.faces.new([v for v, _ in ring] if ring is rings[-1] else [v for v, _ in ring][::-1])
            for lp in f.loops:
                lp[uv].uv = (lp.vert.co.x / tile, lp.vert.co.y / tile)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return finish(bm, mat)

def box_uv_bm(bm, tile, off=(0, 0)):
    uv = bm.loops.layers.uv.get("UVMap") or bm.loops.layers.uv.new("UVMap")
    for f in bm.faces:
        n = f.normal; ax = max(range(3), key=lambda i: abs(n[i]))
        for lp in f.loops:
            c = lp.vert.co
            u, v = {0: (c.y, c.z), 1: (c.x, c.z), 2: (c.x, c.y)}[ax]
            lp[uv].uv = (off[0] + u / tile, off[1] + v / tile)

def slab(center, size, mat, bump=0.0, sub=(1, 1, 1), rot_z=0.0):
    """Quader mit optionaler Beulung (Lehmputz), UV als Würfelprojektion in Weltmaß."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    if any(s > 1 for s in sub):
        bmesh.ops.subdivide_edges(bm, edges=bm.edges, cuts=max(sub) - 1, use_grid_fill=True)
    if bump:
        for v in bm.verts:
            v.co += v.co.normalized() * 0  # Platzhalter für Lesbarkeit
            n = noise.noise(Vector((v.co.x * 1.7 + center[0], v.co.y * 1.7 + center[1], v.co.z * 1.7)))
            # nur Außenflächen beulen (nicht die Kanten an Pfosten)
            v.co.y += bump * n if abs(v.co.y) > size[1] * 0.49 else 0
            v.co.x += bump * n if abs(v.co.x) > size[0] * 0.49 and size[0] < size[1] else 0
    bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=__import__("mathutils").Matrix.Rotation(rot_z, 3, "Z"))
    bmesh.ops.translate(bm, vec=Vector(center), verts=bm.verts)
    bm.normal_update(); box_uv_bm(bm, TEXSETS[mat][1], (rng.random(), rng.random()))
    return finish(bm, mat)

def thatch_roof(L, W, wall_h, pitch_deg, ov_side, ov_end, thick, ridge_extra=0.0):
    """Reetdach aus zwei Flächen mit Dicke, unregelmäßiger Traufe und Beulung. Gibt Firsthöhe (Unterseite) zurück."""
    p = math.radians(pitch_deg)
    z_under_ridge = wall_h + 0.12 + (W / 2) * math.tan(p)
    s_under = (W / 2 + ov_side) / math.cos(p)          # Länge der Unterseite vom First bis zur Traufe
    nx, nu = 34, 14
    for sgn in (-1, 1):
        bm = bmesh.new(); uv = bm.loops.layers.uv.new("UVMap")
        nrm = Vector((0, sgn * math.sin(p), math.cos(p)))
        grid_top, grid_bot = [], []
        for i in range(nx + 1):
            x = -L / 2 - ov_end + (L + 2 * ov_end) * i / nx
            rowt, rowb = [], []
            jag = rng.uniform(-0.07, 0.04)
            for j in range(nu + 1):
                u = s_under * j / nu                       # 0 = First, s_under = Traufe
                if j == nu: u += jag
                base = Vector((x, sgn * u * math.cos(p), z_under_ridge - u * math.sin(p)))
                bulge = 0.035 * noise.noise(Vector((x * 0.9, u * 0.9, sgn * 3.0))) + 0.02 * math.sin(math.pi * j / nu)
                th = thick * (0.75 if j == nu else 1.0)   # Traufe ausgedünnt
                top = base + nrm * (th + bulge)
                bot = base + nrm * (0.0 if j < nu else 0.04)
                # Ortgang (Giebelkante) leicht abgerundet und eingezogen
                if i in (0, nx): top -= nrm * 0.05; top.x += (0.04 if i == 0 else -0.04)
                rowt.append((bm.verts.new(top), x, u)); rowb.append((bm.verts.new(bot), x, u))
            grid_top.append(rowt); grid_bot.append(rowb)
        T = TEXSETS["M_Thatch"][1]; TV = 4.6; off = rng.random()
        def quad(a, b, c, d, flip=False, side=False):
            vs = [a, b, c, d]
            f = bm.faces.new([q[0] for q in (vs[::-1] if flip else vs)])
            for lp in f.loops:
                q = next(q for q in vs if q[0] is lp.vert)
                if side:  # Ortgang: Halme laufen weiter die Schräge hinab, Dicke quer
                    depth = 0.0 if any(q is t for row in grid_top for t in row) else thick
                    lp[uv].uv = (off + depth / T, q[2] / TV)
                else:
                    lp[uv].uv = (off + q[1] / T, q[2] / TV)
        flip = sgn > 0
        for i in range(nx):
            for j in range(nu):
                quad(grid_top[i][j], grid_top[i + 1][j], grid_top[i + 1][j + 1], grid_top[i][j + 1], flip)
                quad(grid_bot[i][j], grid_bot[i][j + 1], grid_bot[i + 1][j + 1], grid_bot[i + 1][j], flip)
        for i in range(nx):  # Traufkante (Stirnseite der Halme)
            quad(grid_top[i][nu], grid_top[i + 1][nu], grid_bot[i + 1][nu], grid_bot[i][nu], flip)
        for i, sideflip in ((0, not flip), (nx, flip)):  # Ortgang
            for j in range(nu):
                quad(grid_top[i][j], grid_top[i][j + 1], grid_bot[i][j + 1], grid_bot[i][j], sideflip, side=True)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        finish(bm, "M_Thatch")
    # Firstwulst
    ridge_top = z_under_ridge + thick / math.cos(p)
    zc = z_under_ridge + thick * math.cos(p) - 0.04; rr = thick * math.sin(p) + 0.06
    rp = [(-L / 2 - ov_end + 0.2 + (L + 2 * ov_end - 0.4) * k / 8, rng.uniform(-0.02, 0.02), zc + rng.uniform(-0.02, 0.02)) for k in range(9)]
    tube(rp, [rr * rng.uniform(0.92, 1.08) for _ in rp], 10, "M_Thatch", jitter=0.15)
    ridge_top = zc + rr
    return z_under_ridge, ridge_top

def wattle_panel(x0, x1, y, z0, z1, axis="x"):
    """Flechtwerk: senkrechte Staken + waagerechte Ruten, die sich um die Staken winden."""
    length = abs(x1 - x0); n_st = max(2, int(length / 0.32) + 1)
    stakes = [x0 + (x1 - x0) * (k + 0.5) / n_st for k in range(n_st)]
    def P(a, off, z):
        return (a, y + off, z) if axis == "x" else (y + off, a, z)
    for k, a in enumerate(stakes):
        tube([P(a, 0, z0 - 0.02), P(a, 0, z1 + 0.04)], [0.022, 0.018], 5, "M_Bark", cap=False)
    zz = z0 + 0.04; row = 0
    while zz < z1 - 0.02:
        pts = []; rad = []
        steps = n_st * 3
        for s in range(steps + 1):
            a = x0 + (x1 - x0) * s / steps
            ph = (s / 3.0 + row) * math.pi
            pts.append(P(a, 0.035 * math.cos(ph), zz + rng.uniform(-0.006, 0.006))); rad.append(0.016)
        tube(pts, rad, 4, "M_Bark", cap=False)
        zz += 0.052; row += 1

def post(x, y, h, r):
    lean = (rng.uniform(-0.03, 0.03), rng.uniform(-0.03, 0.03))
    tube([(x, y, -0.15), (x + lean[0] * 0.5, y + lean[1] * 0.5, h * 0.5), (x + lean[0], y + lean[1], h)], [r * 1.05, r, r * 0.92], 8, "M_Bark", jitter=0.08)

def gable_fill(xe, W, wall_h, z_top, t, outward):
    """Giebeldreieck aus Lehm; oben bleibt ein Rauchabzug offen."""
    bm = bmesh.new()
    zc = z_top - 0.5
    hw = (W / 2) * (z_top - zc) / (z_top - wall_h)
    poly = [(-W / 2, wall_h), (W / 2, wall_h), (hw, zc), (-hw, zc)]
    a = [bm.verts.new((xe - outward * t / 2, y, z)) for y, z in poly]
    b = [bm.verts.new((xe + outward * t / 2, y, z)) for y, z in poly]
    bm.faces.new(a); bm.faces.new(b[::-1])
    for i in range(4):
        j = (i + 1) % 4; bm.faces.new((a[i], a[j], b[j], b[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces); bm.normal_update()
    box_uv_bm(bm, TEXSETS["M_Daub"][1], (rng.random(), rng.random()))
    finish(bm, "M_Daub")

def hide_curtain(cx, y, w, h, z0, axis="x"):
    """Fellvorhang vor der Tür, unten leicht aufgeworfen und zur Seite gerafft."""
    bm = bmesh.new(); uv = bm.loops.layers.uv.new("UVMap"); nx, nz = 8, 8; grid = []
    for i in range(nx + 1):
        col = []
        for j in range(nz + 1):
            u = i / nx; v = j / nz
            gather = (1 - v) * 0.35 * u
            x = cx - w / 2 + w * (u - gather)
            off = -0.06 - 0.05 * math.sin(u * math.pi * 3) * (1 - v) - 0.08 * (1 - v) ** 2
            z = z0 + h * v
            co = (x, y + off, z) if axis == "x" else (y + off, x, z)
            col.append(bm.verts.new(co))
        grid.append(col)
    for i in range(nx):
        for j in range(nz):
            f = bm.faces.new((grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1]))
            for lp, (a, b2) in zip(f.loops, ((i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1))):
                lp[uv].uv = (a / nx * w / 0.9, b2 / nz * h / 0.9)
    o = finish(bm, "M_Hide")
    m = o.modifiers.new("dick", "SOLIDIFY"); m.thickness = 0.012
    bpy.context.view_layer.objects.active = o; bpy.ops.object.modifier_apply(modifier=m.name)

# =============================== Modelle ===============================
def build_house():
    L, W, H = 8.0, 5.6, 1.85
    slab((0, 0, 0.03), (L - 0.1, W - 0.1, 0.1), "M_Earth")                        # Lehmestrich innen
    # Pfosten im Raster ~1,35 m
    nL = 6; nW = 4
    xs = [-L / 2 + L * k / nL for k in range(nL + 1)]; ys = [-W / 2 + W * k / nW for k in range(nW + 1)]
    door_x = (xs[2] + xs[3]) / 2; door_w = 0.95
    posts = set()
    for x in xs:
        for y in (-W / 2, W / 2): posts.add((round(x, 3), y))
    for y in ys:
        for x in (-L / 2, L / 2): posts.add((x, round(y, 3)))
    posts.add((round(door_x - door_w / 2 - 0.1, 3), -W / 2)); posts.add((round(door_x + door_w / 2 + 0.1, 3), -W / 2))
    for (x, y) in posts: post(x, y, H + 0.08, rng.uniform(0.09, 0.11))
    # Rähm (Balken oben auf den Pfosten)
    for y in (-W / 2, W / 2):
        tube([(-L / 2 - 0.25, y, H + 0.1), (0, y + rng.uniform(-0.02, 0.02), H + 0.12), (L / 2 + 0.25, y, H + 0.1)], [0.11, 0.12, 0.11], 8, "M_Bark", jitter=0.06)
    for x in (-L / 2, L / 2):
        tube([(x, -W / 2 - 0.2, H + 0.3), (x, W / 2 + 0.2, H + 0.3)], [0.1, 0.1], 8, "M_Bark", jitter=0.06)
    # Wandfelder: Lehm auf Flechtwerk, einige Felder oben unverputzt (Flechtwerk sichtbar)
    bare_budget = 5
    def panel(a0, a1, wall_y, axis):
        nonlocal bare_budget
        z_daub = H
        if bare_budget > 0 and rng.random() < 0.38:
            z_daub = rng.uniform(0.7, 1.25); bare_budget -= 1
            wattle_panel(a0 + 0.1, a1 - 0.1, wall_y, z_daub - 0.05, H, axis)
        c = ((a0 + a1) / 2, wall_y, z_daub / 2) if axis == "x" else (wall_y, (a0 + a1) / 2, z_daub / 2)
        sz = (abs(a1 - a0) - 0.12, 0.15, z_daub) if axis == "x" else (0.15, abs(a1 - a0) - 0.12, z_daub)
        slab(c, sz, "M_Daub", bump=0.025, sub=(6, 2, 4))
    for i in range(nL):
        a0, a1 = xs[i], xs[i + 1]
        for y in (-W / 2, W / 2):
            if y < 0 and a0 < door_x < a1:
                # Türfeld: links/rechts schmale Wandstücke, Sturz, darüber Lehm
                panel(a0, door_x - door_w / 2 - 0.1, y, "x"); panel(door_x + door_w / 2 + 0.1, a1, y, "x")
                tube([(door_x - door_w / 2 - 0.3, y - 0.02, 1.62), (door_x + door_w / 2 + 0.3, y - 0.02, 1.64)], [0.07, 0.07], 8, "M_Wood")
                slab((door_x, y, (1.66 + H) / 2), (door_w + 0.2, 0.15, H - 1.66), "M_Daub", bump=0.02, sub=(4, 2, 2))
                tube([(door_x - door_w / 2, y - 0.05, 0.02), (door_x + door_w / 2, y - 0.05, 0.02)], [0.06, 0.06], 8, "M_Wood")  # Schwelle
                hide_curtain(door_x, y, door_w - 0.05, 1.5, 0.1, "x")
                continue
            panel(a0, a1, y, "x")
    for i in range(nW):
        for x in (-L / 2, L / 2): panel(ys[i], ys[i + 1], x, "y")
    # Dach
    zr, zt = thatch_roof(L, W, H, 48, 0.5, 0.45, 0.32)
    for xe, o in ((-L / 2, -1), (L / 2, 1)): gable_fill(xe, W, H + 0.12, zr - 0.15, 0.14, o)
    # Firststangen: gekreuzte Stangen halten den Firstwulst (wie R3)
    for k in range(5):
        x = -L / 2 + 0.3 + (L - 0.6) * k / 4
        for sg in (-1, 1):
            tube([(x, sg * 0.75, zt - 0.55), (x + rng.uniform(-0.03, 0.03), -sg * 0.28, zt + 0.42)], [0.045, 0.035], 6, "M_Bark")
    # Windbretter-Kreuz an den Giebeln
    for xe in (-L / 2 - 0.5, L / 2 + 0.5):
        for sg in (-1, 1):
            tube([(xe, sg * 1.2, zt - 1.05), (xe, -sg * 0.35, zt + 0.55)], [0.06, 0.05], 7, "M_Bark")
    return L + 1.5, W + 1.6

def build_tent():
    R, Hc = 2.6, 4.3; n = 13
    apex = Vector((0, 0, Hc))
    for k in range(n):
        a = 2 * math.pi * (k + 0.5) / n + rng.uniform(-0.04, 0.04)
        base = Vector((R * 1.04 * math.cos(a), R * 1.04 * math.sin(a), -0.1))
        d = (apex - base).normalized(); top = apex + d * rng.uniform(0.6, 1.0) + Vector((rng.uniform(-0.08, 0.08), rng.uniform(-0.08, 0.08), 0))
        tube([base, base.lerp(apex, 0.5), apex, top], [0.05, 0.045, 0.038, 0.03], 6, "M_Bark")
    # Fellbahn: Kegel mit Durchhang zwischen den Stangen, oben Rauchöffnung, vorn Türöffnung
    bm = bmesh.new(); uv = bm.loops.layers.uv.new("UVMap")
    na, nh = 52, 16; z0, z1 = 0.12, Hc - 0.55
    door_a, door_hw, door_h = -math.pi / 2, 0.32, 1.45
    grid = []
    for i in range(na + 1):
        a = 2 * math.pi * i / na; col = []
        for j in range(nh + 1):
            z = z0 + (z1 - z0) * j / nh
            r = R * (Hc - z) / Hc
            sag = 1 - 0.035 * (0.5 - 0.5 * math.cos(2 * math.pi * n * i / na)) * (1 - z / Hc)
            hem = 0.03 * noise.noise(Vector((a * 3, 0.5, 0))) if j == 0 else 0
            col.append(bm.verts.new((r * sag * math.cos(a), r * sag * math.sin(a), z + hem)))
        grid.append(col)
    T = TEXSETS["M_Hide"][1]
    for i in range(na):
        for j in range(nh):
            a_mid = 2 * math.pi * (i + 0.5) / na
            z_mid = z0 + (z1 - z0) * (j + 0.5) / nh
            da = math.atan2(math.sin(a_mid - door_a), math.cos(a_mid - door_a))
            if abs(da) < door_hw * (1 - (z_mid / door_h) ** 2) ** 0.5 if z_mid < door_h else False:
                continue  # Türöffnung (oval)
            f = bm.faces.new((grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1]))
            for lp, (ii, jj) in zip(f.loops, ((i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1))):
                aa = 2 * math.pi * ii / na; zz = z0 + (z1 - z0) * jj / nh
                lp[uv].uv = (aa * R / T, zz / T)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for f in bm.faces:
        if f.normal.dot(f.calc_center_median() - Vector((0, 0, f.calc_center_median().z))) < 0: f.normal_flip()
    o = finish(bm, "M_Hide")
    m = o.modifiers.new("dick", "SOLIDIFY"); m.thickness = 0.015; m.offset = -1
    bpy.context.view_layer.objects.active = o; bpy.ops.object.modifier_apply(modifier=m.name)
    # Verschnürung über der Tür (Holzstifte) und Heringe am Boden
    for k in range(7):
        z = door_h + 0.15 + k * 0.32; r = R * (Hc - z) / Hc + 0.03
        tube([(r * math.cos(door_a) - 0.12, r * math.sin(door_a) - 0.02, z), (r * math.cos(door_a) + 0.12, r * math.sin(door_a) - 0.02, z + 0.02)], [0.012, 0.01], 5, "M_Wood")
    for k in range(n):
        a = 2 * math.pi * (k + 0.5) / n + 0.12; rr = R * 1.0 + 0.1
        tube([(rr * math.cos(a), rr * math.sin(a), -0.15), (rr * math.cos(a) * 1.02, rr * math.sin(a) * 1.02, 0.14)], [0.025, 0.02], 5, "M_Wood")
    # Rauchklappen
    for sg in (-1, 1):
        bm = bmesh.new(); a = door_a + sg * 0.55
        pts = [(Hc - 0.6, 0.0), (Hc - 0.6, 0.45 * sg), (Hc + 0.05, 0.25 * sg)]
        vs = []
        for z, da in pts:
            r = R * (Hc - z) / Hc + 0.04
            vs.append(bm.verts.new((r * math.cos(a + da * 0.6), r * math.sin(a + da * 0.6), z)))
        bm.faces.new(vs); box_uv_bm(bm, T); o = finish(bm, "M_Hide")
        m = o.modifiers.new("dick", "SOLIDIFY"); m.thickness = 0.015
        bpy.context.view_layer.objects.active = o; bpy.ops.object.modifier_apply(modifier=m.name)
    slab((0, 0, 0.02), (R * 1.5, R * 1.5, 0.04), "M_Earth")   # festgetretener Boden innen
    return 2 * R + 1.5, 2 * R + 1.5

def build_granary():
    L, W, Hp, Hw = 3.4, 2.6, 0.85, 1.55
    for x in (-L / 2 + 0.2, 0, L / 2 - 0.2):
        for y in (-W / 2 + 0.2, W / 2 - 0.2):
            post(x, y, Hp, 0.11)
            tube([(x, y, Hp - 0.02), (x, y, Hp + 0.06)], [0.2, 0.18], 10, "M_Wood")  # Mäuseteller
    for y in (-W / 2 + 0.2, W / 2 - 0.2):
        tube([(-L / 2 - 0.1, y, Hp + 0.14), (L / 2 + 0.1, y, Hp + 0.14)], [0.09, 0.09], 8, "M_Bark")
    n = int(L / 0.17)
    for k in range(n):  # Bodenbohlen aus Rundhölzern
        x = -L / 2 + 0.1 + (L - 0.2) * k / (n - 1)
        tube([(x, -W / 2 - 0.12, Hp + 0.28), (x + rng.uniform(-0.02, 0.02), W / 2 + 0.12, Hp + 0.28)], [0.075, 0.07], 7, "M_Bark")
    z0 = Hp + 0.34; H = z0 + Hw
    xs = [-L / 2, -L / 6, L / 6, L / 2]; ys = [-W / 2, 0, W / 2]
    for x in xs:
        for y in (-W / 2, W / 2): post(x, y, H + 0.05, 0.075)
    for y in ys:
        for x in (-L / 2, L / 2):
            if y == 0: post(x, y, H + 0.05, 0.075)
    door_x, door_w = 0.0, 0.7
    for i in range(3):
        a0, a1 = xs[i], xs[i + 1]
        for y in (-W / 2, W / 2):
            if y < 0 and i == 1:
                slab((door_x, y, z0 + 1.15 + (Hw - 1.15) / 2), (a1 - a0 - 0.1, 0.12, Hw - 1.15), "M_Daub", bump=0.015, sub=(3, 2, 2))
                tube([(a0, y - 0.03, z0 + 1.12), (a1, y - 0.03, z0 + 1.12)], [0.055, 0.055], 7, "M_Wood")
                bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
                bmesh.ops.scale(bm, vec=Vector((door_w, 0.05, 1.05)), verts=bm.verts)
                bmesh.ops.translate(bm, vec=Vector((door_x, y - 0.03, z0 + 0.55)), verts=bm.verts)
                box_uv_bm(bm, TEXSETS["M_Wood"][1]); finish(bm, "M_Wood")                         # Brettertür
                continue
            slab(((a0 + a1) / 2, y, z0 + Hw / 2), (a1 - a0 - 0.1, 0.12, Hw), "M_Daub", bump=0.02, sub=(4, 2, 3))
    for i in range(2):
        for x in (-L / 2, L / 2):
            slab((x, (ys[i] + ys[i + 1]) / 2, z0 + Hw / 2), (0.12, ys[i + 1] - ys[i] - 0.1, Hw), "M_Daub", bump=0.02, sub=(2, 4, 3))
    for y in (-W / 2, W / 2):
        tube([(-L / 2 - 0.2, y, H + 0.08), (L / 2 + 0.2, y, H + 0.08)], [0.08, 0.08], 8, "M_Bark")
    zr, zt = thatch_roof(L, W, H, 48, 0.55, 0.35, 0.26)
    for xe, o in ((-L / 2, -1), (L / 2, 1)): gable_fill(xe, W, H + 0.12, zr - 0.05, 0.12, o)
    # Kerbbalken-Leiter zur Tür
    top = Vector((door_x + 0.05, -W / 2 - 0.12, z0 - 0.02)); bot = Vector((door_x + 0.25, -W / 2 - 1.05, -0.05))
    tube([bot, bot.lerp(top, 0.5), top], [0.12, 0.11, 0.1], 8, "M_Bark")
    for k in range(4):
        p = bot.lerp(top, (k + 0.6) / 4.6)
        tube([p + Vector((-0.13, 0.06, 0.05)), p + Vector((0.13, 0.06, 0.05))], [0.035, 0.035], 6, "M_Wood")
    return L + 1.4, W + 2.0

span = {"house": build_house, "tent": build_tent, "granary": build_granary}[MODEL]()

# ---------- Zusammenführen: ein Objekt, ein Material-Slot je Material (feste Reihenfolge) ----------
order = [m for m in TEXSETS if m in MATS]
for o in PARTS:
    own = o.data.materials[0].name
    o.data.materials.clear()
    for name in order: o.data.materials.append(MATS[name])
    idx = order.index(own)
    for poly in o.data.polygons: poly.material_index = idx
    for lay in list(o.data.uv_layers):
        if lay.name != "UVMap": o.data.uv_layers.remove(lay)
bpy.ops.object.select_all(action="DESELECT")
for o in PARTS: o.select_set(True)
bpy.context.view_layer.objects.active = PARTS[0]
bpy.ops.object.join()
obj = bpy.context.object; obj.name = NAMES[MODEL]; obj.data.name = NAMES[MODEL]
mats = order
bpy.ops.object.shade_smooth()
try:
    obj.data.set_sharp_from_angle(angle=math.radians(50))
except AttributeError:
    obj.data.use_auto_smooth = True; obj.data.auto_smooth_angle = math.radians(50)
obj.location = (0, 0, 0)
dg = bpy.context.evaluated_depsgraph_get(); me = obj.evaluated_get(dg).to_mesh(); me.calc_loop_triangles()
tris = len(me.loop_triangles); obj.evaluated_get(dg).to_mesh_clear()
bb = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
mn = Vector((min(v.x for v in bb), min(v.y for v in bb), min(v.z for v in bb))); mx = Vector((max(v.x for v in bb), max(v.y for v in bb), max(v.z for v in bb)))
print(f"STATS {NAMES[MODEL]} tris={tris} mats={mats} size_m=({mx.x - mn.x:.2f}, {mx.y - mn.y:.2f}, {mx.z - mn.z:.2f}) zmin={mn.z:.2f}")

os.makedirs(OUTDIR, exist_ok=True)
bpy.ops.export_scene.fbx(filepath=os.path.join(OUTDIR, NAMES[MODEL] + ".fbx"), use_selection=True, object_types={"MESH"},
                         apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                         mesh_smooth_type="FACE", use_mesh_modifiers=True, path_mode="STRIP", add_leaf_bones=False)

if PREVIEW:
    bpy.ops.mesh.primitive_plane_add(size=60); g = bpy.context.object
    g.data.materials.append(material("M_Earth"))
    bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.uv.cube_project(cube_size=1.3 * 2); bpy.ops.object.mode_set(mode="OBJECT")
    sun = bpy.data.lights.new("Sun", "SUN"); sun.energy = 3.2; sun.angle = math.radians(2.5)
    so = bpy.data.objects.new("Sun", sun); scene.collection.objects.link(so); so.rotation_euler = (math.radians(48), 0, math.radians(-38))
    world = bpy.data.worlds.new("W"); scene.world = world; world.use_nodes = True
    bg = world.node_tree.nodes["Background"]; bg.inputs["Color"].default_value = (0.78, 0.80, 0.84, 1); bg.inputs["Strength"].default_value = 0.9
    cam = bpy.data.cameras.new("Cam"); cam.lens = 40
    co = bpy.data.objects.new("Cam", cam); scene.collection.objects.link(co); scene.camera = co
    s = max(mx.x - mn.x, mx.y - mn.y, mx.z - mn.z); h = mx.z
    co.location = (s * 1.25, -s * 1.75, h * 0.55 + s * 0.45)
    d = Vector((0, 0, h * 0.42)) - co.location; co.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    scene.render.engine = "CYCLES"; scene.cycles.samples = SAMPLES; scene.cycles.device = "CPU"; scene.cycles.use_denoising = False
    scene.render.resolution_x = 1200; scene.render.resolution_y = 900; scene.view_settings.view_transform = "AgX"
    scene.view_settings.look = "AgX - Medium High Contrast"
    scene.render.filepath = PREVIEW; bpy.ops.render.render(write_still=True)
