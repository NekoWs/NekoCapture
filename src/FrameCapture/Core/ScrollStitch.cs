namespace FrameCapture.Core;

/// <summary>
/// 滚动截图核心拼接算法（纯函数、可单测）。
/// 场景：同一屏幕区域截了 acc（已累积的长图）与 b（滚动后再截的一帧，同宽）。
/// 向下滚动 dy 像素后，b 的顶部若干行与 acc 的底部若干行是同一内容（重叠），b 底部才是新内容。
/// FindOverlap 找重叠行数 ov，Append 把 b 去掉顶部 ov 行后拼到 acc 下方。
/// 行比对用「降采样灰度行签名」：每行取 cols 个等距列的灰度均值，抗轻微色差、够快。
/// 签名只为 acc 底部 win+1 行（win=min(accH,bH)）构建：候选重叠 ≤ win，容差趟最多再向外探 1 行。
/// 从最大候选重叠往下扫：第一个「有信息行全部匹配」的候选即真重叠，立即返回（偏好最大重叠）。
/// b 侧纯背景行（签名极差 &lt; FlatSpread）在任何纵向偏移下都与同类行相等，无法定位，不参与重叠判定。
/// </summary>
public static class ScrollStitch
{
    /// <summary>b 行签名在 cols 个抽样列上的极差下限：低于此值说明该行整幅同色（纯背景、整幅横线），
    /// 在任何纵向偏移下都与同类行相等，只能给错误候选投赞成票，故不作重叠证据。</summary>
    const int FlatSpread = 8;

    /// <summary>单个候选至少要有这么多有信息抽样行才作判定：不足则既不接受也不记兜底。</summary>
    const int MinEvidence = 4;

    /// <summary>每个候选的抽样行数上限（含首尾均匀取点）：扣掉无信息行后仍要留够判定样本，
    /// 也让「全部匹配」这条判据覆盖足够多的行，偏移一两行的候选才拦得住。</summary>
    const int MaxSamples = 256;

    /// <summary>兜底趟（关掉平坦行过滤）的抽样行数上限：那一趟面对的是整段无信息的画面，
    /// 判定只需粗粒度，扫满候选的代价要压住。</summary>
    const int FallbackSamples = 64;

    /// <summary>为 [rowBase, rowBase+h) 这段行建降采样灰度签名（长度 h*cols，按局部行号存放）。B/G/R 取均值，忽略 A。</summary>
    /// <param name="bgra">源图 BGRA 行主序。</param>
    /// <param name="w">源图宽度（像素）。</param>
    /// <param name="h">要建签名的行数。</param>
    /// <param name="cols">每行抽样列数。</param>
    /// <param name="rowBase">起始绝对行号，0 = 从首行开始。</param>
    /// <returns>长度 h*cols 的签名字节数组，第 y 段行落在 [(y)*cols, (y+1)*cols)。</returns>
    static byte[] RowSignatures(byte[] bgra, int w, int h, int cols, int rowBase)
    {
        var sig = new byte[h * cols];
        for (int y = 0; y < h; y++)
        {
            int rowOff = (rowBase + y) * w * 4, sOff = y * cols;
            for (int c = 0; c < cols; c++)
            {
                int x = (int)((long)c * w / cols);
                if (x >= w) x = w - 1;
                int p = rowOff + x * 4;
                sig[sOff + c] = (byte)((bgra[p] + bgra[p + 1] + bgra[p + 2]) / 3);
            }
        }
        return sig;
    }

    /// <summary>
    /// 找 b 顶部与 acc 底部的纵向重叠行数。
    /// 判据：候选重叠从最大往下扫，每个候选均匀抽样至多 <see cref="MaxSamples"/> 行（含首尾）逐行<b>独立</b>比对。
    /// b 侧纯背景行（签名极差 &lt; <see cref="FlatSpread"/>）在任何纵向偏移下都与同类行相等，无法定位，
    /// 只会给错误候选投赞成票，因此不计入抽样、也不构成证据。
    /// 有信息行<b>全部</b>匹配且证据 ≥ <see cref="MinEvidence"/> 的候选就是真重叠，立即返回；
    /// 只达 ≥80% 的候选记为兜底继续往下扫——个别因闪烁光标/抗锯齿/固定表头而略变的行不足以否掉整段重叠。
    /// 两趟匹配：
    /// ① 严格趟——只比对应行，命中即返回，保证整像素滚动的重叠精确（不多不少一行）。
    /// ② 容差趟——严格趟全军覆没时才启用，每行允许在 acc 里 ±1 行窗口内找匹配，吸收平滑/分数滚动
    /// 的 ±1px 取整抖动；重叠的高估上界因此锁在 1 行（肉眼不可见），不会 gross 裁切。
    /// 整趟既无全匹配也无 ≥80% 候选（重叠区整段落进无信息行，例如滚过一大片空白）时，
    /// 关掉平坦行过滤，按全行口径、每候选至多 <see cref="FallbackSamples"/> 行再扫一趟。
    /// </summary>
    /// <param name="acc">已累积长图，BGRA 行主序。</param>
    /// <param name="w">两帧共同宽度（像素），须 &gt;0。</param>
    /// <param name="accH">acc 的行数。</param>
    /// <param name="b">滚动后再截的一帧，BGRA 行主序，宽度须与 acc 相同。</param>
    /// <param name="bH">b 的行数。</param>
    /// <param name="cols">每行抽样的列数（等距取点），越大越准也越慢。</param>
    /// <param name="thresh">单行平均灰度差阈值（0–255 尺度）：同内容行 ≈0，错位行 ≫ 此值。</param>
    /// <returns>重叠行数，范围 0..min(accH,bH)；0 = 没找到可靠重叠，调用方按「整帧都是新内容」处理。</returns>
    public static int FindOverlap(byte[] acc, int w, int accH, byte[] b, int bH, int cols = 64, double thresh = 16.0) =>
        Overlap(acc, w, accH, b, bH, cols, thresh, exactOnly: false);

    /// <summary>
    /// 只认「有信息行<b>全部</b>匹配」的高置信重叠：≥80% 兜底与关掉平坦行过滤的兜底趟都不启用，凑不出全匹配就返回 0。
    /// 滚动<b>途中</b>抓到的帧走这个口径——过渡帧可能只与累积底部部分吻合，按可疑重叠追加会跳过整段新内容，
    /// 宁可不拼这一拍、等下一帧。停稳后的帧用 <see cref="FindOverlap"/>，容差趟与兜底照常生效。
    /// </summary>
    /// <param name="acc">已累积长图，BGRA 行主序。</param>
    /// <param name="w">两帧共同宽度（像素），须 &gt;0。</param>
    /// <param name="accH">acc 的行数。</param>
    /// <param name="b">滚动后再截的一帧，BGRA 行主序，宽度须与 acc 相同。</param>
    /// <param name="bH">b 的行数。</param>
    /// <param name="cols">每行抽样的列数（等距取点），越大越准也越慢。</param>
    /// <param name="thresh">单行平均灰度差阈值（0–255 尺度）。</param>
    /// <returns>全匹配的重叠行数；凑不出全匹配候选时为 0，调用方应跳过这一帧而不是按 0 追加。</returns>
    public static int FindOverlapExact(byte[] acc, int w, int accH, byte[] b, int bH, int cols = 64, double thresh = 16.0) =>
        Overlap(acc, w, accH, b, bH, cols, thresh, exactOnly: true);

    static int Overlap(byte[] acc, int w, int accH, byte[] b, int bH, int cols, double thresh, bool exactOnly)
    {
        if (w <= 0 || accH <= 0 || bH <= 0) return 0;
        int win = Math.Min(accH, bH);                 // 重叠不可能超过任一帧高，也界定搜索窗口
        // 候选重叠 ≤ win 行、容差趟再向外探 1 行，签名只需覆盖 acc 底部 win+1 行
        int aBaseRow = Math.Max(0, accH - win - 1);
        int aRows = accH - aBaseRow;
        var sigA = RowSignatures(acc, w, aRows, cols, aBaseRow);
        var sigB = RowSignatures(b, w, bH, cols, 0);
        int maxCheck = Math.Min(win, 3000);           // 无匹配时最多检查这么多候选，防长图卡顿

        // b 侧每行签名的极差：极差小的行整幅同色，纵向平移后仍与同类行相等，定位不了重叠。
        var informative = new bool[bH];
        for (int y = 0; y < bH; y++)
        {
            int off = y * cols, lo = 255, hi = 0;
            for (int c = 0; c < cols; c++) { byte v = sigB[off + c]; if (v < lo) lo = v; if (v > hi) hi = v; }
            informative[y] = hi - lo >= FlatSpread;
        }

        // 抽样行号：samples 个点均匀铺在 [0, ov) 上，含首尾两行。
        static int SampleRow(int s, int samples, int ov) =>
            samples == 1 ? 0 : (int)((long)s * (ov - 1) / (samples - 1));

        // 单行匹配：tol=0 只比对应行；tol=1 允许 acc 行号 ±1 窗口内任一行达标（吸收 ±1px 抖动）。
        // sigA 原点在 aBaseRow：取 acc 绝对行 ar 的签名要减 aBaseRow 换成局部行号。
        bool RowMatch(int aRow, int bRow, int tol)
        {
            int bi = bRow * cols;
            for (int d = -tol; d <= tol; d++)
            {
                int ar = aRow + d;
                if (ar < 0 || ar >= accH) continue;
                int ai = (ar - aBaseRow) * cols;
                double rd = 0;
                for (int c = 0; c < cols; c++) rd += Math.Abs(sigA[ai + c] - sigB[bi + c]);
                if (rd / cols <= thresh) return true;
            }
            return false;
        }

        // 一趟扫描。filterFlat=true 只认有信息行；false 是整趟一无所获时的兜底，按全部抽样行判定。
        int Scan(int tol, bool filterFlat)
        {
            int fallback = 0;                         // 首个 ≥80% 匹配的候选，扫完全无全匹配时才用它
            for (int ov = win; ov >= 1; ov--)
            {
                if (win - ov >= maxCheck) break;      // 只检查最接近 win 的 maxCheck 个候选
                int aBase = accH - ov;
                int samples = Math.Min(ov, filterFlat ? MaxSamples : FallbackSamples);
                int total = samples;                  // 参与判定的抽样行数
                if (filterFlat)
                {
                    total = 0;
                    for (int s = 0; s < samples; s++) if (informative[SampleRow(s, samples, ov)]) total++;
                    if (total < MinEvidence) continue;  // 证据不足：既不接受也不记兜底
                }
                int good = 0, bad = 0;
                for (int s = 0; s < samples; s++)
                {
                    int r = SampleRow(s, samples, ov);
                    if (filterFlat && !informative[r]) continue;
                    if (RowMatch(aBase + r, r, tol)) good++;
                    else if (++bad * 5 > total) break;  // 失配已超两成：全匹配和兜底都够不到了
                }
                if (good == total) return ov;         // 有信息行全匹配 → 最大可靠重叠，就是真值
                if (!exactOnly && good * 5 >= total * 4)
                {
                    if (!filterFlat) return ov;       // 兜底趟按全行口径，首个 ≥80% 即命中
                    if (fallback == 0) fallback = ov; // 过滤趟记下它，继续往下扫等全匹配的真值
                }
            }
            // 整趟既无全匹配也无 ≥80% 候选（重叠区整段落进无信息行）→ 关掉过滤按全行口径再扫一趟
            if (filterFlat && fallback == 0 && !exactOnly) return Scan(tol, false);
            return fallback;
        }

        int strict = Scan(0, true);
        return strict != 0 ? strict : Scan(1, true);
    }

    /// <summary>把 b 去掉顶部 ov 行后拼到 acc 下方。无新内容原样返回。</summary>
    /// <param name="acc">已累积长图，BGRA 行主序。</param>
    /// <param name="w">宽度（像素）。</param>
    /// <param name="accH">acc 的行数。</param>
    /// <param name="b">待拼入的新帧，宽度须与 acc 相同。</param>
    /// <param name="bH">b 的行数。</param>
    /// <param name="ov">要跳过的 b 顶部行数，来自 <see cref="FindOverlap"/>。</param>
    /// <returns>新 BGRA 缓冲，高 accH + bH − ov；bH ≤ ov 时返回 acc 本身（不复制）。</returns>
    public static byte[] Append(byte[] acc, int w, int accH, byte[] b, int bH, int ov)
    {
        int newRows = bH - ov;
        if (newRows <= 0) return acc;
        int newH = accH + newRows;
        var outBuf = new byte[newH * w * 4];
        Buffer.BlockCopy(acc, 0, outBuf, 0, accH * w * 4);
        Buffer.BlockCopy(b, ov * w * 4, outBuf, accH * w * 4, newRows * w * 4);
        return outBuf;
    }
}
