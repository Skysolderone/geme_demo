using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Siege.Core.PieceRelics;

/// <summary>读取档案 v2 的结果类别（缺失、损坏、版本更新沿用现行口径，另加 v1 迁移）。</summary>
public enum RelicProfileLoadStatus
{
    /// <summary>正常读取。</summary>
    Loaded,

    /// <summary>档案不存在，已新建新档案（赠送 3 件普通信物）。</summary>
    Created,

    /// <summary>档案损坏：原文件已改名备份，已重置为新档案。</summary>
    CorruptReset,

    /// <summary>档案来自更新的版本：只读，文件不被写回，本局按关闭带入带出进行。</summary>
    NewerVersion,

    /// <summary>读到 v1 档案：补给已按原价折成补给点、残留在途记录已按中途退出清除，已写回 version 2。</summary>
    Migrated,
}

/// <summary>读取档案的结果：类别、档案路径、损坏时的备份路径与原因、版本更新时的文件版本。</summary>
public sealed record RelicProfileLoad(RelicProfileLoadStatus Status, string Path, string? BackupPath = null, string? Reason = null, int? FileVersion = null)
{
    /// <summary>给用户的提示（终端与图形版共用）。</summary>
    public string Message => Status switch
    {
        RelicProfileLoadStatus.Loaded => $"档案：{Path}",
        RelicProfileLoadStatus.Created => $"已新建档案：{Path}",
        RelicProfileLoadStatus.CorruptReset => $"档案损坏，已备份并重置（{Reason}）。备份：{BackupPath}",
        RelicProfileLoadStatus.NewerVersion =>
            $"档案 {Path} 来自更新的版本（格式版本 {FileVersion}，本程序支持 {RelicProfile.CurrentVersion}）：档案保持不变，本局不提供带入、局终不结算。",
        RelicProfileLoadStatus.Migrated => $"档案已从旧版补给档案升级（补给已按原价折成补给点）：{Path}",
        _ => throw new ArgumentOutOfRangeException(nameof(Status), Status, "未知读取结果。"),
    };
}

/// <summary>
/// 档案 v2 存取（relic-redesign design.md D7）：读（含 v1 迁移）、兑换、开局扣除、结算。与旧 <c>Carry.CarryProfileStore</c> 并存，接线段再切换入口。
/// </summary>
/// <remarks>
/// <para>写入先写同目录的 <c>.tmp</c>，再用 <see cref="File.Move(string, string, bool)"/> 覆盖原文件：替换之前中断时原档案保持写入前的状态。内存中的档案只在写入成功后更新。</para>
/// <para>损坏时先把原文件改名为 <c>&lt;原文件名&gt;.corrupt-&lt;yyyyMMdd-HHmmss&gt;</c> 备份再重置；版本更新时只读、不写回。时钟由入口注入（Core 永不读时钟），只用于备份文件名。</para>
/// <para>同一局只结算一次：结算以在途记录的对局标识去重（在途记录已清除或标识不匹配即忽略）。</para>
/// </remarks>
public sealed class RelicProfileStore
{
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<string>? _beforeReplace;
    private RelicProfile? _profile;
    private bool _readOnly;

    /// <param name="path">档案路径（入口给出）。</param>
    /// <param name="clock">损坏备份文件名用的本地时钟（入口注入）。</param>
    public RelicProfileStore(string path, Func<DateTimeOffset> clock)
        : this(path, clock, beforeReplace: null)
    {
    }

    /// <param name="beforeReplace">测试接缝：临时文件写完、替换原文件之前调用（参数为临时文件路径），用来模拟"替换前进程终止"。</param>
    internal RelicProfileStore(string path, Func<DateTimeOffset> clock, Action<string>? beforeReplace)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(clock);
        FilePath = path;
        _clock = clock;
        _beforeReplace = beforeReplace;
    }

    public string FilePath { get; }

    /// <summary>档案来自更新的版本：只读，任何写入都被拒绝。</summary>
    public bool IsReadOnly => _readOnly;

    /// <summary>当前档案（内存与磁盘一致）。未读取或只读时抛出。</summary>
    public RelicProfile Current => _profile ?? throw new InvalidOperationException(
        _readOnly ? $"档案 {FilePath} 来自更新的版本，本程序不读取其内容。" : "档案尚未读取（先调用 Load）。");

    /// <summary>
    /// 读取档案：不存在时新建新档案（送 3 件普通）；v1 时迁移并立即写回 version 2；损坏时先备份原文件再写新档案；版本更新时不写任何文件、进入只读。
    /// </summary>
    public RelicProfileLoad Load()
    {
        _readOnly = false;
        _profile = null;
        if (!File.Exists(FilePath))
        {
            Save(RelicProfile.New);
            return new RelicProfileLoad(RelicProfileLoadStatus.Created, FilePath);
        }

        string text = File.ReadAllText(FilePath, Encoding.UTF8);
        try
        {
            RelicProfile profile = RelicProfile.Parse(text, out bool migrated);
            if (migrated)
            {
                Save(profile);
                return new RelicProfileLoad(RelicProfileLoadStatus.Migrated, FilePath);
            }

            _profile = profile;
            return new RelicProfileLoad(RelicProfileLoadStatus.Loaded, FilePath);
        }
        catch (RelicProfileVersionException ex)
        {
            _readOnly = true;
            return new RelicProfileLoad(RelicProfileLoadStatus.NewerVersion, FilePath, FileVersion: ex.FileVersion);
        }
        catch (FormatException ex)
        {
            // MUST NOT 在没有备份的情况下覆盖无法解析的档案：先改名备份（备份名已被占用时另取序号），再写新档案。
            string backup = BackupPath();
            File.Move(FilePath, backup);
            Save(RelicProfile.New);
            return new RelicProfileLoad(RelicProfileLoadStatus.CorruptReset, FilePath, backup, ex.Message);
        }
    }

    /// <summary>
    /// 兑换 1 件普通信物（R18）：8 点 / 件（未校准）。补给点足够时扣点、库存 +1 并立即写入，返回 <c>true</c>；
    /// 稀有信物不可兑换、补给点不足时拒绝，<paramref name="refusal"/> 给出原因，档案不变。
    /// </summary>
    public bool TryExchange(RelicKind kind, out string? refusal)
    {
        RelicProfile profile = Writable();
        if (RelicKinds.TierOf(kind) != RelicTier.Common)
        {
            refusal = $"{kind.DisplayName()}是稀有信物，只能局内带出，不能兑换";
            return false;
        }

        int price = RelicKinds.UncalibratedExchangePrice;
        if (profile.Points < price)
        {
            refusal = $"补给点不足（需要 {price}，现有 {profile.Points}）";
            return false;
        }

        Save(profile.WithStock(kind, 1) with { Points = profile.Points - price });
        refusal = null;
        return true;
    }

    /// <summary>
    /// 开局：校验带入清单（≤ 5 件、保险 ≤ 2、库存足够），从库存扣除全部带入件，写入在途记录。
    /// 残留的在途记录必须先由 <see cref="SettleAbandoned"/> 处理。清单不合规即抛 <see cref="InvalidOperationException"/>，档案不变。
    /// </summary>
    public void Begin(string matchId, RelicLoadout loadout)
    {
        ArgumentException.ThrowIfNullOrEmpty(matchId);
        ArgumentNullException.ThrowIfNull(loadout);
        RelicProfile profile = Writable();
        if (profile.InFlight is { } residual)
        {
            throw new InvalidOperationException($"档案还有对局 {residual.MatchId} 的在途记录，须先按中途退出结算。");
        }

        if (loadout.RefusalAgainst(profile) is { } refusal)
        {
            throw new InvalidOperationException($"不能带入：{refusal}。");
        }

        RelicProfile begun = profile;
        foreach (CarriedRelic item in loadout.Items)
        {
            begun = begun.WithStock(item.Kind, -1);
        }

        Save(begun with { InFlight = new RelicInFlight(matchId, loadout.Items) });
    }

    /// <summary>
    /// 把残留的在途记录按"中途退出"结算（R16 / D6）：只回保险件、带出 0 点，清除在途记录。返回被结算的记录；没有残留时返回 <c>null</c>、不写档案。
    /// </summary>
    public RelicInFlight? SettleAbandoned()
    {
        RelicProfile profile = Writable();
        if (profile.InFlight is not { } abandoned)
        {
            return null;
        }

        RelicProfile settled = profile;
        foreach (CarriedRelic item in abandoned.Carried.Where(i => i.Insured))
        {
            settled = settled.WithStock(item.Kind, 1);
        }

        Save(settled with { InFlight = null });
        return abandoned;
    }

    /// <summary>
    /// 把本机玩家的一次带出结算写入档案：补给点加上 <see cref="RelicPlayerSettlement.Points"/>，<see cref="RelicPlayerSettlement.BroughtOut"/> 的每件进库存，清除在途记录。
    /// 同一局只结算一次：在途记录已清除或对局标识不匹配时忽略并返回 <c>false</c>。
    /// </summary>
    public bool Settle(string matchId, RelicPlayerSettlement result)
    {
        ArgumentNullException.ThrowIfNull(result);
        RelicProfile profile = Writable();
        if (profile.InFlight is not { } flight || flight.MatchId != matchId)
        {
            return false;
        }

        RelicProfile settled = profile;
        foreach (RelicToken token in result.BroughtOut)
        {
            settled = settled.WithStock(token.Kind, 1);
        }

        Save(settled with { Points = profile.Points + result.Points, InFlight = null });
        return true;
    }

    private RelicProfile Writable() => IsReadOnly
        ? throw new InvalidOperationException($"档案 {FilePath} 来自更新的版本，本程序不写回。")
        : Current;

    /// <summary>原子写入：先写同目录临时文件，再替换原文件；成功后才更新内存中的档案。</summary>
    private void Save(RelicProfile profile)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(FilePath));
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        string temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, profile.ToJson(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        _beforeReplace?.Invoke(temporary);
        File.Move(temporary, FilePath, overwrite: true);
        _profile = profile;
    }

    private string BackupPath()
    {
        string stamp = _clock().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string backup = $"{FilePath}.corrupt-{stamp}";
        for (int n = 2; File.Exists(backup); n++)
        {
            backup = $"{FilePath}.corrupt-{stamp}-{n}";
        }

        return backup;
    }
}
