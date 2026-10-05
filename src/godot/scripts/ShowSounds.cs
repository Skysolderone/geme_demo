using System.Collections.Immutable;
using Godot;
using Siege.Presentation.Show;

namespace Siege.Godot;

/// <summary>
/// 结算演出的占位音效（show-sound-cues design.md D2 / D3）：启动时按下面一张参数表把六段短音<b>程序合成</b>成内存里的 16-bit 单声道 PCM
/// （44.1 kHz，不引入任何音频文件），挂一个 4 个 <see cref="AudioStreamPlayer"/> 的池；每帧由 <see cref="SoundCues.Between"/> 导出的提示按序号依次占用空闲播放器，
/// 同帧多于池容量的丢弃；提速期间音量 −9 dB。
/// </summary>
/// <remarks>
/// <para><c>--mute</c> 与无人值守（零时长模式）下本节点<b>根本不创建</b>（GameRoot 里判定），因而不合成波形、场景树里没有任何音频节点。</para>
/// <para>占位音只为把节奏立起来：后期换正式素材时整张 <see cref="Table"/> 换成文件资源即可，播放规则不动。噪声用固定种子的线性同余伪随机，
/// 与规则随机流无关、每次启动逐样本相同。</para>
/// </remarks>
public partial class ShowSounds : Node
{
    /// <summary>播放器池大小（D3）。</summary>
    public const int PoolSize = 4;

    /// <summary>提速期间的音量（dB，D3）。</summary>
    public const float FastVolumeDb = -9f;

    /// <summary>采样率（Hz）。</summary>
    public const int MixRate = 44100;

    private enum Wave
    {
        Sine,
        Triangle,
        Noise,
    }

    /// <summary>一段占位音的合成参数：波形、起止频率（双音在中点切换）、时长、每秒衰减（越大越快；0 = 不衰减）、峰值。</summary>
    private readonly record struct Voice(SoundCueKind Cue, Wave Wave, double StartHz, double EndHz, int DurationMs, double DecayPerSecond, double Gain);

    /// <summary>D2 参数表（时长 60–250 ms 为设计值，横幅 400 ms）。</summary>
    private static readonly Voice[] Table =
    [
        new(SoundCueKind.Placement, Wave.Triangle, 880, 880, 60, 50, 0.45),     // 落子"嗒"：三角波 + 快速衰减
        new(SoundCueKind.Capture, Wave.Noise, 0, 0, 120, 25, 0.5),              // 提子"啪"：噪声 + 衰减
        new(SoundCueKind.Relic, Wave.Sine, 1320, 1760, 180, 8, 0.4),            // 信物"叮"：正弦双音 1320 → 1760
        new(SoundCueKind.Territory, Wave.Sine, 440, 440, 200, 12, 0.5),         // 领地到账"咣"：正弦 + 中等衰减
        new(SoundCueKind.Group, Wave.Sine, 330, 330, 250, 6, 0.8),              // 军势到账：更低、更慢衰减、音量更大
        new(SoundCueKind.Banner, Wave.Sine, 220, 220, 400, 4, 0.6),             // 横幅低音
    ];

    private readonly AudioStreamWav[] _streams = new AudioStreamWav[Table.Length];
    private readonly AudioStreamPlayer[] _players = new AudioStreamPlayer[PoolSize];

    /// <summary>已合成的波形段数（启动日志用）。</summary>
    public int VoiceCount => Table.Length;

    /// <inheritdoc/>
    public override void _Ready()
    {
        for (int i = 0; i < Table.Length; i++)
        {
            _streams[i] = Synthesize(Table[i]);
        }

        for (int i = 0; i < PoolSize; i++)
        {
            var player = new AudioStreamPlayer { Name = $"Cue{i}" };
            _players[i] = player;
            AddChild(player);
        }
    }

    /// <summary>按序号依次占用空闲播放器播放本帧的提示；没有空闲播放器时丢弃其余（D3）。</summary>
    public void Play(ImmutableArray<SoundCue> cues, bool fast)
    {
        if (cues.IsDefaultOrEmpty)
        {
            return;
        }

        float volume = fast ? FastVolumeDb : 0f;
        foreach (SoundCue cue in cues)
        {
            // tiered-number-show 段 A：提示带档位、新增"揭示"一种。揭示短音与按档升调在段 B 接入，此前只有"揭示"这一种不发声；
            // 豁免只点名这一种——其余种类缺表仍由 IndexOf 抛出（响亮失败），不因这条过渡而被一并吞掉。段 B 给参数表补上揭示后删掉这个分支。
            if (cue.Kind == SoundCueKind.Reveal)
            {
                continue;
            }

            int voice = IndexOf(cue.Kind);
            AudioStreamPlayer? free = System.Array.Find(_players, p => !p.Playing);
            if (free is null)
            {
                return;
            }

            free.Stream = _streams[voice];
            free.VolumeDb = volume;
            free.Play();
        }
    }

    private static int IndexOf(SoundCueKind cue)
    {
        for (int i = 0; i < Table.Length; i++)
        {
            if (Table[i].Cue == cue)
            {
                return i;
            }
        }

        throw new System.ArgumentOutOfRangeException(nameof(cue), cue, "参数表里没有这种提示。");
    }

    /// <summary>把一条参数合成成 16-bit 单声道 PCM：5 ms 起音避免爆音，随后按指数衰减；双音在时长中点切换频率。</summary>
    private static AudioStreamWav Synthesize(Voice voice)
    {
        int samples = MixRate * voice.DurationMs / 1000;
        int attack = MixRate * 5 / 1000;
        var data = new byte[samples * 2];
        uint noise = 0x9E3779B9u ^ (uint)voice.Cue;   // 固定种子：每次启动逐样本相同
        double phase = 0d;
        for (int i = 0; i < samples; i++)
        {
            double t = (double)i / MixRate;
            double hz = i * 2 < samples ? voice.StartHz : voice.EndHz;
            phase += hz / MixRate;
            phase -= System.Math.Floor(phase);
            double raw = voice.Wave switch
            {
                Wave.Sine => System.Math.Sin(phase * System.Math.Tau),
                Wave.Triangle => (4d * System.Math.Abs(phase - 0.5d)) - 1d,
                _ => NextNoise(ref noise),
            };

            double envelope = System.Math.Exp(-voice.DecayPerSecond * t);
            if (i < attack)
            {
                envelope *= (double)i / attack;
            }

            // 末尾 5 ms 线性收尾，衰减慢的段也不会在切断处爆音。
            int tail = samples - i;
            if (tail < attack)
            {
                envelope *= (double)tail / attack;
            }

            short sample = (short)System.Math.Round(System.Math.Clamp(raw * envelope * voice.Gain, -1d, 1d) * short.MaxValue);
            data[i * 2] = (byte)(sample & 0xFF);
            data[(i * 2) + 1] = (byte)((sample >> 8) & 0xFF);
        }

        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = MixRate,
            Stereo = false,
            LoopMode = AudioStreamWav.LoopModeEnum.Disabled,
            Data = data,
        };
    }

    /// <summary>线性同余伪随机 → [−1, 1)：确定性、不消费规则随机流。</summary>
    private static double NextNoise(ref uint state)
    {
        state = (state * 1664525u) + 1013904223u;
        return ((state >> 8) / (double)(1u << 24) * 2d) - 1d;
    }
}
