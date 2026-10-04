using Godot;

namespace Siege.Godot;

/// <summary>
/// 程序建模工具（map-elements-v2 D1）：基于 <see cref="SurfaceTool"/> 的静态函数，全部输出 <see cref="ArrayMesh"/>，平直着色（逐面法线）。
/// 地图部件（<see cref="LowPoly"/> 里的地块、侧面等）用它建模，再由 <see cref="PartExport"/> 烘成资源。
/// </summary>
/// <remarks>
/// <para><b>确定性</b>：抖动与色差一律取自 <see cref="Hash"/>（整数散列，再换算成浮点偏移），不用随机数——同一 seed 产出逐项相同的顶点数组。</para>
/// <para><b>顶点色只当亮度系数用</b>：最终颜色 = 材质底色 × 顶点色，材质仍按底色共用（<see cref="Visuals.Shaded"/>），压暗、淡染、明暗格照旧只改底色。
/// 顶点色在显存里是 8 位（0–1），存不下大于 1 的系数，所以系数 1.0 存成 <see cref="NominalByte"/>，
/// 材质底色乘回 <see cref="ShadeCeiling"/>（<see cref="Albedo"/>）：系数 1.0 的面与不带顶点色时逐位同色，最亮可到 <see cref="ShadeCeiling"/>。</para>
/// <para>原点约定：<see cref="BeveledSlab"/> 与 <see cref="Strata"/> 的原点在<b>顶面中心</b>，实体向下延伸；<see cref="FacetRock"/> 在球心。都不出底面（看不见）。</para>
/// </remarks>
public static class LowPolyMesh
{
    /// <summary>亮度系数 1.0 对应的顶点色字节值。</summary>
    private const int NominalByte = 232;

    /// <summary>亮度系数的上限（约 1.099）：材质底色乘它，顶点色除它。</summary>
    public const float ShadeCeiling = 255f / NominalByte;

    /// <summary>带顶点色的材质该设的底色：线性空间里把 <paramref name="baseColor"/> 乘 <see cref="ShadeCeiling"/>，使系数 1.0 的面仍是 <paramref name="baseColor"/>。</summary>
    public static Color Albedo(Color baseColor)
    {
        Color linear = baseColor.SrgbToLinear();
        return new Color(linear.R * ShadeCeiling, linear.G * ShadeCeiling, linear.B * ShadeCeiling, baseColor.A).LinearToSrgb();
    }

    /// <summary><see cref="Albedo"/> 的逆：由材质底色读回原底色（导出时给共享材质起名用）。</summary>
    public static Color BaseOf(Color albedo)
    {
        Color linear = albedo.SrgbToLinear();
        return new Color(linear.R / ShadeCeiling, linear.G / ShadeCeiling, linear.B / ShadeCeiling, albedo.A).LinearToSrgb();
    }

    /// <summary>整数散列：同一 (<paramref name="seed"/>, <paramref name="index"/>) 永远得到同一个值。</summary>
    public static uint Hash(int seed, int index)
    {
        unchecked
        {
            uint h = ((uint)seed * 0x9E3779B1u) ^ ((uint)index * 0x85EBCA6Bu) ^ 0xC2B2AE35u;
            h ^= h >> 15;
            h *= 0x2C1B3C6Du;
            h ^= h >> 12;
            h *= 0x297A2D39u;
            h ^= h >> 15;
            return h;
        }
    }

    /// <summary>散列换算成 [0, 1) 的浮点（取高 24 位，换算无舍入误差）。</summary>
    public static float Unit(int seed, int index) => (Hash(seed, index) >> 8) / 16777216f;

    /// <summary>散列换算成 [-1, 1) 的浮点。</summary>
    public static float Signed(int seed, int index) => (Unit(seed, index) * 2f) - 1f;

    /// <summary>
    /// 顶面四边倒角的板（地砖顶板、衬底、铺面；石沿与边框也用它）。占 x、z ∈ ±size/2，y ∈ [−size.Y, 0]。
    /// <paramref name="bevel"/> 是倒角的水平收进量与下沉量（45°），取 0 即普通方板。
    /// <paramref name="facet"/> 大于 0 时顶面分成 8 个三角小面，各带 ±<paramref name="facet"/> 的亮度细差（切分点与细差由 <paramref name="seed"/> 散列）。
    /// 倒角面比顶面略亮、立面略暗。
    /// </summary>
    public static ArrayMesh BeveledSlab(Vector3 size, float bevel, float shade = 1f, int seed = 0, float facet = 0f) =>
        new Builder().Slab(size, bevel, shade, seed, facet).Commit();

    /// <summary>
    /// 侧面分层的实心块（高度侧面）。占 x、z ∈ ±size/2，y ∈ [−size.Y, 0]，不越出这个盒子。
    /// 按高度分 <paramref name="bands"/> 条带：每条带亮度不同，并各自向内收进 0–<paramref name="jitter"/>，条带之间露出窄台阶。
    /// <paramref name="ledge"/> 为真时最上一条带顶到盒子边缘、其余条带至少收进 <paramref name="jitter"/>——顶边读作一圈外突的岩沿。
    /// <paramref name="cracks"/> 是每面墙上的竖向深色岩缝条数（位置、宽度、贯穿几条带由散列定）。
    /// </summary>
    public static ArrayMesh Strata(Vector3 size, int bands, float jitter, int seed, bool ledge = false, int cracks = 0) =>
        new Builder().Strata(size, bands, jitter, seed, ledge, cracks).Commit();

    /// <summary>
    /// 多面岩块（岩石、碎石、垂岩）：低细分球按散列抖动顶点半径，逐面平直着色并带细微亮度差。原点在球心。
    /// <paramref name="facets"/> 是每圈的顶点数（≥ 3）；<paramref name="jitter"/> 是半径抖动的比例；
    /// <paramref name="scale"/> 把压扁 / 拉长直接烘进顶点——部件节点上只留刚体变换，合批与逐格两种画法受光才一致。
    /// </summary>
    public static ArrayMesh FacetRock(float radius, int facets, float jitter, int seed, Vector3? scale = null) =>
        new Builder().At(new Transform3D(Basis.FromScale(scale ?? Vector3.One), Vector3.Zero)).Rock(radius, facets, jitter, seed).Commit();

    /// <summary>
    /// 程序化直棱柱（低多边形硬边）：<paramref name="profile"/> 是 XY 平面上的凸多边形，沿 Z 轴居中拉伸 <paramref name="depth"/>。
    /// 每个面按"朝外"方向定绕序与法线，不依赖输入多边形的顺逆时针。不带顶点色（棋子用它，材质不读顶点色）。
    /// </summary>
    public static ArrayMesh Prism(Vector2[] profile, float depth)
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        float half = depth * 0.5f;

        Vector2 centroid = Vector2.Zero;
        foreach (Vector2 p in profile)
        {
            centroid += p;
        }

        centroid /= profile.Length;
        for (int i = 1; i + 1 < profile.Length; i++)
        {
            PrismFace(tool, At(profile[0], half), At(profile[i], half), At(profile[i + 1], half), Vector3.Back);
            PrismFace(tool, At(profile[0], -half), At(profile[i], -half), At(profile[i + 1], -half), Vector3.Forward);
        }

        for (int i = 0; i < profile.Length; i++)
        {
            Vector2 a = profile[i];
            Vector2 b = profile[(i + 1) % profile.Length];
            Vector2 edge = b - a;
            var outward2 = new Vector2(edge.Y, -edge.X);
            if (outward2.Dot(((a + b) * 0.5f) - centroid) < 0f)
            {
                outward2 = -outward2;
            }

            var outward = new Vector3(outward2.X, outward2.Y, 0f);
            PrismFace(tool, At(a, half), At(b, half), At(b, -half), outward);
            PrismFace(tool, At(a, half), At(b, -half), At(a, -half), outward);
        }

        return tool.Commit();

        static Vector3 At(Vector2 p, float z) => new(p.X, p.Y, z);
    }

    /// <summary>
    /// 细条（裂纹、水纹、芦苇等贴地或立起的条）：沿折线 <paramref name="points"/> 铺一条宽 <paramref name="width"/> 的带，
    /// 带面朝 <paramref name="normal"/>（贴地的条传朝上，立起的条传水平方向）。<paramref name="twoSided"/> 为真时正反两面都出。
    /// </summary>
    public static ArrayMesh Ribbon(Vector3[] points, float width, Vector3 normal, float shade = 1f, bool twoSided = false) =>
        new Builder().Ribbon(points, width, normal, shade, twoSided).Commit();

    /// <summary>
    /// 建模器：把若干基本形状合进<b>同一个网格</b>（一件部件里同一材质的几何合成一个网格，明暗靠亮度系数区分，子网格数与绘制调用都少）。
    /// <see cref="At(Transform3D)"/> 设定之后各形状的摆放——变换直接烘进顶点，法线按变换后的几何重算，所以非均匀缩放也受光正确。
    /// </summary>
    public sealed class Builder
    {
        private readonly SurfaceTool _tool = Begin();
        private Transform3D _at = Transform3D.Identity;

        /// <summary>之后加入的形状都先经这个变换。</summary>
        public Builder At(Transform3D transform)
        {
            _at = transform;
            return this;
        }

        /// <summary>同 <see cref="At(Transform3D)"/>：位置 + 欧拉角（度，与节点的 RotationDegrees 同一约定）+ 缩放。</summary>
        public Builder At(Vector3 position, Vector3 rotationDegrees = default, Vector3? scale = null)
        {
            var radians = new Vector3(Mathf.DegToRad(rotationDegrees.X), Mathf.DegToRad(rotationDegrees.Y), Mathf.DegToRad(rotationDegrees.Z));
            return At(new Transform3D(Basis.FromEuler(radians).Scaled(scale ?? Vector3.One), position));
        }

        public ArrayMesh Commit() => _tool.Commit();

        /// <summary>见 <see cref="LowPolyMesh.BeveledSlab"/>。</summary>
        public Builder Slab(Vector3 size, float bevel, float shade = 1f, int seed = 0, float facet = 0f)
        {
            float hx = size.X * 0.5f, hz = size.Z * 0.5f;
            float ix = hx - bevel, iz = hz - bevel;

            if (facet > 0f)
            {
                // 顶面：中心点与四条边的中点各按散列挪一点，绕中心扇出 8 个小面。
                var center = new Vector3(ix * 0.30f * Signed(seed, 0), 0f, iz * 0.30f * Signed(seed, 1));
                Vector3[] ring =
                [
                    new(-ix, 0f, -iz), new(ix * 0.35f * Signed(seed, 2), 0f, -iz),
                    new(ix, 0f, -iz), new(ix, 0f, iz * 0.35f * Signed(seed, 3)),
                    new(ix, 0f, iz), new(ix * 0.35f * Signed(seed, 4), 0f, iz),
                    new(-ix, 0f, iz), new(-ix, 0f, iz * 0.35f * Signed(seed, 5)),
                ];
                for (int i = 0; i < ring.Length; i++)
                {
                    Tri(center, ring[i], ring[(i + 1) % ring.Length], Vector3.Up, Shade(shade * (1f + (facet * Signed(seed, 16 + i)))));
                }
            }
            else
            {
                Quad(new(-ix, 0f, -iz), new(ix, 0f, -iz), new(ix, 0f, iz), new(-ix, 0f, iz), Vector3.Up, Shade(shade));
            }

            // 四边：倒角面 + 立面。(sx, sz) 是这条边的朝外方向。
            foreach ((int sx, int sz) in Sides)
            {
                var outward = new Vector3(sx, 0f, sz);
                (Vector3 a, Vector3 b) = Edge(sx, sz, ix, iz, 0f);
                (Vector3 c, Vector3 d) = Edge(sx, sz, hx, hz, -bevel);
                (Vector3 e, Vector3 f) = Edge(sx, sz, hx, hz, -size.Y);
                if (bevel > 0f)
                {
                    Quad(a, b, d, c, outward + Vector3.Up, Shade(shade * 1.05f));
                }

                Quad(c, d, f, e, outward, Shade(shade * 0.95f));
            }

            return this;
        }

        /// <summary>见 <see cref="LowPolyMesh.Strata"/>。</summary>
        public Builder Strata(Vector3 size, int bands, float jitter, int seed, bool ledge = false, int cracks = 0)
        {
            float hx = size.X * 0.5f, hz = size.Z * 0.5f;

            // 条带的上下边界（自顶向下）与各自的收进量、亮度。
            float[] levels = new float[bands + 1];
            float[] inset = new float[bands];
            float[] shade = new float[bands];
            float step = size.Y / bands;
            for (int k = 0; k <= bands; k++)
            {
                float wobble = k == 0 || k == bands ? 0f : step * 0.22f * Signed(seed, 40 + k);
                levels[k] = -(k * step) + wobble;
            }

            for (int k = 0; k < bands; k++)
            {
                inset[k] = ledge ? (k == 0 ? 0f : jitter * (1f + (0.6f * Unit(seed, 60 + k)))) : jitter * Unit(seed, 60 + k);
                shade[k] = (k % 2 == 0 ? 1.03f : 0.94f) + (0.025f * Signed(seed, 80 + k));
            }

            Quad(new(-hx + inset[0], 0f, -hz + inset[0]), new(hx - inset[0], 0f, -hz + inset[0]), new(hx - inset[0], 0f, hz - inset[0]), new(-hx + inset[0], 0f, hz - inset[0]),
                Vector3.Up, Shade(1.03f));

            int wall = 0;
            foreach ((int sx, int sz) in Sides)
            {
                var outward = new Vector3(sx, 0f, sz);
                for (int k = 0; k < bands; k++)
                {
                    (Vector3 top0, Vector3 top1) = Edge(sx, sz, hx - inset[k], hz - inset[k], levels[k]);
                    (Vector3 low0, Vector3 low1) = Edge(sx, sz, hx - inset[k], hz - inset[k], levels[k + 1]);

                    // 这条带沿墙方向的分段：岩缝是墙面上的深色竖条（共面，不挖几何，不会漏缝）。
                    var cuts = new List<(float From, float To)>();
                    for (int c = 0; c < cracks; c++)
                    {
                        int id = (wall * 16) + c;
                        int reach = 1 + (int)(Hash(seed, 200 + id) % (uint)bands);
                        if (k >= reach)
                        {
                            continue;
                        }

                        float lane = (c + 0.5f) / cracks;
                        float at = lane + (0.28f / cracks * Signed(seed, 220 + id)) + (0.018f * Signed(seed, 240 + (id * 8) + k));
                        float half = 0.014f + (0.010f * Unit(seed, 260 + id));
                        cuts.Add((Math.Clamp(at - half, 0.04f, 0.96f), Math.Clamp(at + half, 0.04f, 0.96f)));
                    }

                    cuts.Sort((p, q) => p.From.CompareTo(q.From));
                    float cursor = 0f;
                    foreach ((float from, float to) in cuts)
                    {
                        if (from <= cursor)
                        {
                            continue;
                        }

                        Quad(top0.Lerp(top1, cursor), top0.Lerp(top1, from), low0.Lerp(low1, from), low0.Lerp(low1, cursor), outward, Shade(shade[k]));
                        Quad(top0.Lerp(top1, from), top0.Lerp(top1, to), low0.Lerp(low1, to), low0.Lerp(low1, from), outward, Shade(0.72f));
                        cursor = to;
                    }

                    Quad(top0.Lerp(top1, cursor), top1, low1, low0.Lerp(low1, cursor), outward, Shade(shade[k]));

                    // 与下一条带之间的台阶：下面的更靠外则台阶朝上（看得见，略亮），否则朝下（悬挑的底面，略暗）。
                    if (k + 1 < bands && inset[k + 1] != inset[k])
                    {
                        (Vector3 next0, Vector3 next1) = Edge(sx, sz, hx - inset[k + 1], hz - inset[k + 1], levels[k + 1]);
                        bool up = inset[k + 1] < inset[k];
                        Quad(low0, low1, next1, next0, up ? Vector3.Up : Vector3.Down, Shade(up ? 1.06f : 0.80f));
                    }
                }

                wall++;
            }

            return this;
        }

        /// <summary>多面岩块，原点在球心（见 <see cref="LowPolyMesh.FacetRock"/>）；压扁 / 拉长经 <see cref="At(Transform3D)"/> 的缩放烘进顶点。</summary>
        public Builder Rock(float radius, int facets, float jitter, int seed, float shade = 1f)
        {
            int rings = Math.Max(2, facets / 2);

            Vector3 Point(int ring, int j)
            {
                // ring 0 = 顶极点，rings + 1 = 底极点；中间各圈相邻两圈错开半格。
                if (ring == 0 || ring == rings + 1)
                {
                    float pole = radius * (1f + (jitter * Signed(seed, ring == 0 ? 1 : 2)));
                    return new Vector3(0f, ring == 0 ? pole : -pole, 0f);
                }

                int column = ((j % facets) + facets) % facets;
                float lat = Mathf.Pi * ring / (rings + 1);
                float lon = Mathf.Tau * (column + (0.5f * (ring % 2))) / facets;
                float r = radius * (1f + (jitter * Signed(seed, (ring * 64) + column)));
                return new Vector3(r * Mathf.Sin(lat) * Mathf.Cos(lon), r * Mathf.Cos(lat), r * Mathf.Sin(lat) * Mathf.Sin(lon));
            }

            int face = 0;
            void Face(Vector3 a, Vector3 b, Vector3 c) => Tri(a, b, c, a + b + c, Shade(shade * (1f + (0.07f * Signed(seed, 1000 + face++)))));

            for (int j = 0; j < facets; j++)
            {
                Face(Point(0, 0), Point(1, j), Point(1, j + 1));
                Face(Point(rings + 1, 0), Point(rings, j), Point(rings, j + 1));
                for (int ring = 1; ring < rings; ring++)
                {
                    // 相邻两圈错开半格：奇数圈的第 j 个点在偶数圈第 j 与 j+1 个点之间。
                    int shift = ring % 2;
                    Face(Point(ring, j), Point(ring, j + 1), Point(ring + 1, j + shift));
                    Face(Point(ring + 1, j + shift), Point(ring + 1, j + shift - 1 + (2 * (1 - shift))), Point(ring, j + 1 - shift));
                }
            }

            return this;
        }

        /// <summary>方盒，原点在盒心。顶面取 <paramref name="shade"/>，立面略暗，底面更暗。</summary>
        public Builder Box(Vector3 size, float shade = 1f)
        {
            float hx = size.X * 0.5f, hy = size.Y * 0.5f, hz = size.Z * 0.5f;
            Quad(new(-hx, hy, -hz), new(hx, hy, -hz), new(hx, hy, hz), new(-hx, hy, hz), Vector3.Up, Shade(shade));
            Quad(new(-hx, -hy, -hz), new(hx, -hy, -hz), new(hx, -hy, hz), new(-hx, -hy, hz), Vector3.Down, Shade(shade * 0.85f));
            foreach ((int sx, int sz) in Sides)
            {
                (Vector3 a, Vector3 b) = Edge(sx, sz, hx, hz, hy);
                (Vector3 c, Vector3 d) = Edge(sx, sz, hx, hz, -hy);
                Quad(a, b, d, c, new Vector3(sx, 0f, sz), Shade(shade * 0.96f));
            }

            return this;
        }

        /// <summary>
        /// 棱台 / 棱锥（树干、树冠、立柱、垂岩）：原点在高度中点（与内置圆柱同一约定），底面半径 <paramref name="bottom"/>、顶面半径 <paramref name="top"/>（取 0 即尖顶）。
        /// 各侧面带 ±<paramref name="facet"/> 的亮度细差。
        /// </summary>
        public Builder Cone(float bottom, float top, float height, int sides, float shade = 1f, int seed = 0, float facet = 0f)
        {
            float half = height * 0.5f;
            Vector3 Rim(int i, float radius, float y)
            {
                float angle = Mathf.Tau * (((i % sides) + sides) % sides) / sides;
                return new Vector3(radius * Mathf.Cos(angle), y, radius * Mathf.Sin(angle));
            }

            var apex = new Vector3(0f, half, 0f);
            var floor = new Vector3(0f, -half, 0f);
            for (int i = 0; i < sides; i++)
            {
                Color color = Shade(shade * (1f + (facet * Signed(seed, i))));
                Vector3 b0 = Rim(i, bottom, -half), b1 = Rim(i + 1, bottom, -half);
                Vector3 outward = b0 + b1 + new Vector3(0f, bottom - top, 0f);
                if (top > 0f)
                {
                    Vector3 t0 = Rim(i, top, half), t1 = Rim(i + 1, top, half);
                    Quad(b0, b1, t1, t0, outward, color);
                    Tri(apex, t0, t1, Vector3.Up, Shade(shade * 1.04f));
                }
                else
                {
                    Tri(b0, b1, apex, outward, color);
                }

                if (bottom > 0f)
                {
                    Tri(floor, b0, b1, Vector3.Down, Shade(shade * 0.85f));
                }
            }

            return this;
        }

        /// <summary>贴地的不规则多边形片（水洼、卵石的顶面）：在 y = 0 平面上、面朝上，各顶点半径按散列抖动。</summary>
        public Builder Disc(float radius, int sides, int seed, float jitter, float shade = 1f)
        {
            Vector3 Point(int i)
            {
                int k = ((i % sides) + sides) % sides;
                float angle = Mathf.Tau * k / sides;
                float r = radius * (1f + (jitter * Signed(seed, k)));
                return new Vector3(r * Mathf.Cos(angle), 0f, r * Mathf.Sin(angle));
            }

            for (int i = 0; i < sides; i++)
            {
                Tri(Vector3.Zero, Point(i), Point(i + 1), Vector3.Up, Shade(shade * (1f + (0.04f * Signed(seed, 32 + i)))));
            }

            return this;
        }

        /// <summary>见 <see cref="LowPolyMesh.Ribbon"/>。</summary>
        public Builder Ribbon(Vector3[] points, float width, Vector3 normal, float shade = 1f, bool twoSided = false)
        {
            var side = new Vector3[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 along = points[Math.Min(i + 1, points.Length - 1)] - points[Math.Max(i - 1, 0)];
                side[i] = along.Cross(normal).Normalized() * (width * 0.5f);
            }

            Color color = Shade(shade);
            for (int i = 0; i + 1 < points.Length; i++)
            {
                Vector3 a = points[i] - side[i], b = points[i] + side[i], c = points[i + 1] + side[i + 1], d = points[i + 1] - side[i + 1];
                Quad(a, b, c, d, normal, color);
                if (twoSided)
                {
                    Quad(a, b, c, d, -normal, color);
                }
            }

            return this;
        }

        /// <summary>
        /// 把 XY 平面上的一条折线 <paramref name="profile"/> 沿 Z 轴居中拉成一张宽 <paramref name="depth"/> 的折板（嵌入亮条这类贴着别的形状走的薄片）。
        /// 每一段的正面朝折线前进方向的左侧。
        /// </summary>
        public Builder Sheet(Vector2[] profile, float depth, float shade = 1f)
        {
            float half = depth * 0.5f;
            Color color = Shade(shade);
            for (int i = 0; i + 1 < profile.Length; i++)
            {
                Vector2 p = profile[i], q = profile[i + 1];
                var left = new Vector3(-(q.Y - p.Y), q.X - p.X, 0f);
                Quad(new(p.X, p.Y, -half), new(q.X, q.Y, -half), new(q.X, q.Y, half), new(p.X, p.Y, half), left, color);
            }

            return this;
        }

        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, Color color)
        {
            Tri(a, b, c, outward, color);
            Tri(a, c, d, outward, color);
        }

        /// <summary>一个带色三角面：先过当前变换；法线取变换后的几何法线（朝 <paramref name="outward"/> 那一侧），绕序按 Godot 的"顺时针为正面"调整。</summary>
        private void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward, Color color)
        {
            a = _at * a;
            b = _at * b;
            c = _at * c;
            outward = _at.Basis * outward;
            Vector3 cross = (b - a).Cross(c - a);
            bool counterClockwise = cross.Dot(outward) > 0f;
            Vector3 normal = (counterClockwise ? cross : -cross).Normalized();
            foreach (Vector3 vertex in (Vector3[])[a, counterClockwise ? c : b, counterClockwise ? b : c])
            {
                _tool.SetNormal(normal);
                _tool.SetColor(color);
                _tool.AddVertex(vertex);
            }
        }
    }

    // 四条边的朝外方向：+X、−X、+Z、−Z。
    private static readonly (int Sx, int Sz)[] Sides = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    /// <summary>半边长为 (<paramref name="hx"/>, <paramref name="hz"/>) 的矩形在 (sx, sz) 那条边上的两个端点，高度 <paramref name="y"/>。</summary>
    private static (Vector3 A, Vector3 B) Edge(int sx, int sz, float hx, float hz, float y) => sx != 0
        ? (new Vector3(sx * hx, y, -hz), new Vector3(sx * hx, y, hz))
        : (new Vector3(-hx, y, sz * hz), new Vector3(hx, y, sz * hz));

    private static SurfaceTool Begin()
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        return tool;
    }

    /// <summary>亮度系数 → 顶点色（灰阶，系数 1.0 存成 <see cref="NominalByte"/>）。</summary>
    private static Color Shade(float factor)
    {
        int value = Math.Clamp((int)MathF.Round(factor * NominalByte), 0, 255);
        return Color.Color8((byte)value, (byte)value, (byte)value);
    }

    /// <summary>棱柱的一个三角面：Godot 以顺时针（从正面看）为正面，按期望的朝外法线调整绕序。</summary>
    private static void PrismFace(SurfaceTool tool, Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
    {
        Vector3 normal = outward.Normalized();
        bool counterClockwise = (b - a).Cross(c - a).Dot(outward) > 0f;
        tool.SetNormal(normal);
        tool.AddVertex(a);
        tool.SetNormal(normal);
        tool.AddVertex(counterClockwise ? c : b);
        tool.SetNormal(normal);
        tool.AddVertex(counterClockwise ? b : c);
    }
}
