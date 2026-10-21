extends SceneTree

# 用法：godot --headless --path src/godot --script <本文件> -- <部件目录>
# 对每件点缀：把所有 MeshInstance3D 的三角面按节点变换到部件原点坐标系，在每个三角面上按重心坐标网格采样（含顶点与边），
# 统计：最高 y、|x| / |z| 最大值、格心半径 0.36 圆柱内采样点的最高 y、y > 0.02 的采样点离格心的最小水平距离、三角形数、surface 数。
const PREFIXES = ["rock_", "trees_", "desert_", "marsh_", "crag_", "shallows_"]
const N = 10
const BASE = 0.36
const FLOOR = 0.02

func _init():
	var args = OS.get_cmdline_user_args()
	var dir = args[0]
	var files = DirAccess.get_files_at(dir)
	files.sort()
	for f in files:
		if not f.ends_with(".tscn"):
			continue
		var ok = false
		for p in PREFIXES:
			if f.begins_with(p):
				ok = true
		if not ok:
			continue
		var scene = load(dir + "/" + f)
		var root = scene.instantiate()
		var st = {"maxy": -1e9, "miny": 1e9, "ax": 0.0, "az": 0.0, "inner": -1e9, "near": 1e9, "tris": 0, "surf": 0}
		_walk(root, root.transform, st)
		print("[check] %s surfaces=%d tris=%d maxY=%.4f minY=%.4f max|x|=%.4f max|z|=%.4f innerMaxY(r<0.36)=%.4f nearestHigh(y>0.02)=%.4f" % [
			f, st.surf, st.tris, st.maxy, st.miny, st.ax, st.az, st.inner if st.inner > -1e8 else -1.0, st.near])
		root.free()
	quit(0)

func _walk(node, xf, st):
	if node is MeshInstance3D and node.mesh != null:
		var mesh = node.mesh
		for s in mesh.get_surface_count():
			st.surf += 1
			var arrays = mesh.surface_get_arrays(s)
			var verts = arrays[Mesh.ARRAY_VERTEX]
			var idx = arrays[Mesh.ARRAY_INDEX]
			var count = idx.size() if idx != null and idx.size() > 0 else verts.size()
			for t in range(0, count, 3):
				var a = verts[idx[t]] if idx != null and idx.size() > 0 else verts[t]
				var b = verts[idx[t + 1]] if idx != null and idx.size() > 0 else verts[t + 1]
				var c = verts[idx[t + 2]] if idx != null and idx.size() > 0 else verts[t + 2]
				st.tris += 1
				for i in range(N + 1):
					for j in range(N + 1 - i):
						var p = xf * (a + (b - a) * (float(i) / N) + (c - a) * (float(j) / N))
						var r = sqrt(p.x * p.x + p.z * p.z)
						st.maxy = max(st.maxy, p.y)
						st.miny = min(st.miny, p.y)
						st.ax = max(st.ax, abs(p.x))
						st.az = max(st.az, abs(p.z))
						if r < BASE:
							st.inner = max(st.inner, p.y)
						if p.y > FLOOR:
							st.near = min(st.near, r)
	for child in node.get_children():
		_walk(child, xf * child.transform, st)
