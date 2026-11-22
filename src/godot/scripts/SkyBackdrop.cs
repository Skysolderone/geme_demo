using Godot;

namespace Siege.Godot;

/// <summary>
/// 对局背景天空（chibi-world 段 ③ R3.1）：负责人看图选定 image（2026-11-22，段 ③ 裁决 1），为缺省；toon 留作调试参数 <c>--sky=toon</c>，不删。
/// <list type="bullet">
/// <item><c>image</c>（缺省）：保留原画 <c>art/sky/floating-realm-panorama.png</c>，只在画布着色器里调色——粉 / 紫色相拉向天蓝并去饱和（云的粉影变成淡蓝影、远山变成蓝灰），天空拉向蔚蓝，云的桃色去饱和，其余色相提饱和、略加对比，向色板 A 的饱和取向靠拢。</item>
/// <item><c>toon</c>：不用图片，画布着色器现场画——四阶分段的竖向渐变天空 + 静止的卡通云（亮面 / 暗面两阶，外缘一圈深天蓝细线，不用墨色），底部一条云海。</item>
/// </list>
/// 两版都是 Layer −1 的画布背景（环境 <c>BGMode.Canvas</c>），不动灯光、环境光、色调映射与 SSAO；不用随机数、云不动，同一机位截图可复现。
/// </summary>
public static class SkyBackdrop
{
    /// <summary>天空的两版。</summary>
    public enum Style
    {
        /// <summary>原画调色。</summary>
        Image,

        /// <summary>代码绘制的渐变天空 + 卡通云。</summary>
        Toon,
    }

    /// <summary>原画路径（导出包里是导入后的纹理）。</summary>
    public const string ImagePath = "res://art/sky/floating-realm-panorama.png";

    private static Texture2D? _imageTexture;
    private static ShaderMaterial? _gradeMaterial;
    private static ShaderMaterial? _toonMaterial;

    /// <summary>本进程用哪一版（缺省 image）；GameRoot 在建局前按 <c>--sky=</c> 设定。</summary>
    public static Style Current { get; private set; } = Style.Image;

    /// <summary>当前版的名字（与参数值相同）。</summary>
    public static string Name => Current == Style.Toon ? "toon" : "image";

    /// <summary>当前版的一句说明（启动日志 <c>[sky]</c> 行用）。</summary>
    public static string Intent => Current == Style.Toon
        ? "代码绘制：四阶分段渐变天空 + 静止卡通云（亮 / 暗两阶、深天蓝细边）+ 底部云海"
        : "原画调色：粉紫色相拉向天蓝并去饱和、天空拉向蔚蓝（饱和 ×1.18）、云的桃色去饱和，其余色相饱和 ×1.30、对比 ×1.06";

    /// <summary>按名字（image / toon，不区分大小写）选定本进程的天空；不认识返回 false、不改动。</summary>
    public static bool TrySelect(string name)
    {
        switch (name.Trim().ToLowerInvariant())
        {
            case "image":
                Current = Style.Image;
                return true;
            case "toon":
                Current = Style.Toon;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// 搭一层背景画布（Layer −1）。image 版缺图时返回 <c>null</c>，调用方退回引擎的程序天空（与改前缺图时相同）。
    /// </summary>
    public static CanvasLayer? Build()
    {
        Control? backdrop = Current == Style.Toon ? ToonRect() : ImageRect();
        if (backdrop is null)
        {
            return null;
        }

        var layer = new CanvasLayer { Name = "RealmBackdrop", Layer = -1 };
        backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(backdrop);
        return layer;
    }

    private static TextureRect? ImageRect()
    {
        Texture2D? texture = LoadImage();
        if (texture is null)
        {
            return null;
        }

        return new TextureRect
        {
            Name = "SkyImage",
            Texture = texture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Material = GradeMaterial,
        };
    }

    private static ColorRect ToonRect() => new()
    {
        Name = "SkyToon",
        Color = Colors.White,
        MouseFilter = Control.MouseFilterEnum.Ignore,
        Material = ToonMaterial,
    };

    /// <summary>原画纹理：先取导入后的资源；直接从源码目录启动、尚未导入时读原始 PNG（与改前同一套退路）。</summary>
    private static Texture2D? LoadImage()
    {
        if (_imageTexture is not null)
        {
            return _imageTexture;
        }

        if (ResourceLoader.Exists(ImagePath))
        {
            _imageTexture = GD.Load<Texture2D>(ImagePath);
        }

        if (_imageTexture is null && global::Godot.FileAccess.FileExists(ImagePath))
        {
            Image source = Image.LoadFromFile(ImagePath);
            if (!source.IsEmpty())
            {
                _imageTexture = ImageTexture.CreateFromImage(source);
            }
        }

        return _imageTexture;
    }

    /// <summary>
    /// 原画调色：HSV 里按色相分三段处理——
    /// 粉 / 品红（色相 0.86–1.0 与 0–0.03，云的粉影）：色相拉到天蓝 0.58、饱和压到 ×0.35，读作白云的淡蓝影；
    /// 淡紫（0.70–0.86，远山与雾）：色相拉向 0.62 的蓝灰，饱和 ×1.1；
    /// 天空（0.44–0.66）：色相拉向蔚蓝 0.575（拉 60%）、饱和 ×1.18；低饱和暖色（云的桃色亮面）饱和 ×0.55；
    /// 其余（岛上的绿与暖岩）：饱和 ×1.30。最后以 0.5 为中点对比 ×1.06。各段交界用 smoothstep 过渡，不出色带。
    /// </summary>
    private static ShaderMaterial GradeMaterial => _gradeMaterial ??= new ShaderMaterial
    {
        Shader = new Shader
        {
            Code = """
                shader_type canvas_item;
                uniform float saturation = 1.30;
                uniform float contrast = 1.06;
                uniform float pink_hue = 0.58;
                uniform float pink_saturation = 0.35;
                uniform float lilac_hue = 0.62;
                uniform float sky_hue = 0.575;
                uniform float sky_saturation = 1.18;

                vec3 rgb2hsv(vec3 c) {
                    vec4 k = vec4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                    vec4 p = mix(vec4(c.bg, k.wz), vec4(c.gb, k.xy), step(c.b, c.g));
                    vec4 q = mix(vec4(p.xyw, c.r), vec4(c.r, p.yzx), step(p.x, c.r));
                    float d = q.x - min(q.w, q.y);
                    float e = 1.0e-10;
                    return vec3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
                }

                vec3 hsv2rgb(vec3 c) {
                    vec3 p = abs(fract(c.xxx + vec3(1.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0);
                    return c.z * mix(vec3(1.0), clamp(p - 1.0, 0.0, 1.0), c.y);
                }

                void fragment() {
                    vec4 src = texture(TEXTURE, UV);
                    vec3 hsv = rgb2hsv(src.rgb);
                    float h = hsv.x;
                    // 粉段权重：0.86–1.0 与 0–0.03 两头（色相环绕）。
                    float pink = max(smoothstep(0.84, 0.88, h), 1.0 - smoothstep(0.02, 0.05, h));
                    // 淡紫段权重：0.70–0.86。
                    float lilac = smoothstep(0.68, 0.72, h) * (1.0 - smoothstep(0.84, 0.88, h));
                    float other = clamp(1.0 - pink - lilac, 0.0, 1.0);
                    // 其余色相里再分两类：天空（0.44–0.66）色相拉向蔚蓝 sky_hue、饱和 ×sky_saturation，不让青天发绿；
                    // 低饱和的暖色（0.03–0.14、饱和 < 0.3，云的桃色亮面）饱和 ×0.55，云读作白。
                    float skyw = smoothstep(0.42, 0.46, h) * (1.0 - smoothstep(0.64, 0.68, h));
                    float pale_warm = (1.0 - smoothstep(0.12, 0.16, h)) * (1.0 - smoothstep(0.18, 0.30, hsv.y));
                    float sat_mul = mix(mix(saturation, sky_saturation, skyw), 0.55, pale_warm);
                    vec3 graded_other = vec3(mix(h, sky_hue, skyw * 0.6), clamp(hsv.y * sat_mul, 0.0, 1.0), hsv.z);
                    vec3 graded_pink = vec3(pink_hue, hsv.y * pink_saturation, hsv.z);
                    vec3 graded_lilac = vec3(lilac_hue, clamp(hsv.y * 1.1, 0.0, 1.0), hsv.z);
                    vec3 rgb = hsv2rgb(graded_other) * other + hsv2rgb(graded_pink) * pink + hsv2rgb(graded_lilac) * lilac;
                    rgb = clamp((rgb - 0.5) * contrast + 0.5, 0.0, 1.0);
                    COLOR = vec4(rgb, src.a);
                }
                """,
        },
    };

    /// <summary>
    /// 卡通天空：竖向四阶分段渐变（上深下浅，色值见 uniform），加七朵静止的卡通云与底部云海。
    /// 云 = 若干圆的并集、底边削平；亮面白、下沿一条淡蓝暗面（第二阶），外缘一圈深天蓝细线（宽 <c>rim</c>，按屏高计）。
    /// 纵横比取 <c>SCREEN_PIXEL_SIZE</c>，窗口改尺寸时云不变形。
    /// </summary>
    private static ShaderMaterial ToonMaterial => _toonMaterial ??= new ShaderMaterial
    {
        Shader = new Shader
        {
            Code = """
                shader_type canvas_item;
                uniform vec3 band0 : source_color = vec3(0.180, 0.494, 0.855);
                uniform vec3 band1 : source_color = vec3(0.278, 0.596, 0.910);
                uniform vec3 band2 : source_color = vec3(0.420, 0.718, 0.949);
                uniform vec3 band3 : source_color = vec3(0.627, 0.835, 0.976);
                uniform vec3 cloud_lit : source_color = vec3(0.992, 0.996, 1.000);
                uniform vec3 cloud_shade : source_color = vec3(0.765, 0.847, 0.949);
                uniform vec3 cloud_rim : source_color = vec3(0.243, 0.447, 0.722);
                uniform float rim = 0.0045;

                // 一朵云：圆心 c（x 已乘纵横比）、半径 r 的五个鼓包，底边 y = c.y + 0.35·s 削平。返回有符号距离（负 = 云内）。
                float cloud(vec2 p, vec2 c, float s) {
                    float d = length(p - (c + vec2(-0.55, 0.12) * s)) - 0.36 * s;
                    d = min(d, length(p - (c + vec2(-0.18, -0.10) * s)) - 0.48 * s);
                    d = min(d, length(p - (c + vec2(0.30, -0.02) * s)) - 0.42 * s);
                    d = min(d, length(p - (c + vec2(0.68, 0.16) * s)) - 0.30 * s);
                    d = min(d, length(p - (c + vec2(0.05, 0.18) * s)) - 0.40 * s);
                    return max(d, p.y - (c.y + 0.35 * s));
                }

                void fragment() {
                    float aspect = SCREEN_PIXEL_SIZE.y / SCREEN_PIXEL_SIZE.x;
                    vec2 uv = UV;
                    // 天空四阶：0–0.22 / 0.22–0.44 / 0.44–0.66 / 0.66 以下。
                    float t = uv.y;
                    vec3 sky = t < 0.22 ? band0 : t < 0.44 ? band1 : t < 0.66 ? band2 : band3;

                    float a = aspect;
                    vec2 p = vec2(uv.x * a, uv.y);
                    float d = 1.0e3;
                    float shade_line = 0.0;
                    // 七朵云：左上、右上两朵大的贴边，中部偏下三朵中等，左右下角两朵小的。
                    vec3 clouds[7] = vec3[7](
                        vec3(0.10 * a, 0.16, 0.13), vec3(0.88 * a, 0.12, 0.15), vec3(0.62 * a, 0.30, 0.08),
                        vec3(0.30 * a, 0.42, 0.09), vec3(0.95 * a, 0.52, 0.11), vec3(0.06 * a, 0.62, 0.10),
                        vec3(0.48 * a, 0.70, 0.07));
                    for (int i = 0; i < 7; i++) {
                        float di = cloud(p, clouds[i].xy, clouds[i].z);
                        if (di < d) {
                            d = di;
                            shade_line = clouds[i].y + 0.12 * clouds[i].z;
                        }
                    }

                    // 底部云海：一排鼓包，顶在屏高 0.84 上下起伏。
                    float sea = 1.0e3;
                    for (int k = 0; k < 9; k++) {
                        float x = (float(k) / 8.0) * a;
                        float r = 0.07 + 0.03 * fract(float(k) * 0.618);
                        sea = min(sea, length(p - vec2(x, 0.92)) - r);
                    }
                    sea = min(sea, 0.94 - p.y);
                    if (sea < d) {
                        d = sea;
                        shade_line = 0.93;
                    }

                    // 云内：外缘 rim 宽一圈细线，往里按 shade_line 分亮 / 暗两阶。
                    vec3 col = sky;
                    if (d < 0.0) {
                        col = d > -rim ? cloud_rim : (p.y > shade_line ? cloud_shade : cloud_lit);
                    }
                    COLOR = vec4(col, 1.0);
                }
                """,
        },
    };
}
