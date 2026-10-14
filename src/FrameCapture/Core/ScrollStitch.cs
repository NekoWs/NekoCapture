namespace FrameCapture.Core;

/// <summary>
/// 滚动截图核心拼接算法（纯函数、可单测）。
/// 场景：同一屏幕区域截了 acc（已累积的长图）与 b（滚动后再截的一帧，同宽）。
/// 向下滚动 dy 像素后，b 的顶部若干行与 acc 的底部若干行是同一内容（重叠），b 底部才是新内容。
/// FindOverlap 找重叠行数 ov，Append 把 b 去掉顶部 ov 行后拼到 acc 下方。
/// 行比对用「降采样灰度行签名」：每行取 cols 个等距列的灰度均值，抗轻微色差、够快。
/// 签名只为 acc 底部 win+1 行（win=min(accH,bH)）构建：候选重叠 ≤ win，容差趟最多再向外探 1 行。
/// 从最大候选重叠往下扫、命中第一个即返回（偏好最大重叠，且滚动量小时命中快）。
/// </summary>
public static class ScrollStitch
{
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
    /// 判据：候选重叠从最大往下扫，每个候选均匀抽样至多 32 行（含首尾）逐行<b>独立</b>比对，
    /// ≥80% 抽样行匹配才收——个别因闪烁光标/抗锯齿而略变的行不足以否掉整段重叠。
    /// 两趟匹配：
    /// ① 严格趟——只比对应行，命中即返回，保证整像素滚动的重叠精确（不多不少一行）。
    /// ② 容差趟——严格趟全军覆没时才启用，每行允许在 acc 里 ±1 行窗口内找匹配，吸收平滑/分数滚动
    /// 的 ±1px 取整抖动；重叠的高估上界因此锁在 1 行（肉眼不可见），不会 gross 裁切。
    /// </summary>
    /// <param name="acc">已累积长图，BGRA 行主序。</param>
    /// <param name="w">两帧共同宽度（像素），须 &gt;0。</param>
    /// <param name="accH">acc 的行数。</param>
    /// <param name="b">滚动后再截的一帧，BGRA 行主序，宽度须与 acc 相同。</param>
    /// <param name="bH">b 的行数。</param>
    /// <param name="cols">每行抽样的列数（等距取点），越大越准也越慢。</param>
    /// <param name="thresh">单行平均灰度差阈值（0–255 尺度）：同内容行 ≈0，错位行 ≫ 此值。</param>
    /// <returns>重叠行数，范围 0..min(accH,bH)；0 = 没找到可靠重叠，调用方按「整帧都是新内容」处理。</returns>
    public static int FindOverlap(byte[] acc, int w, int accH, byte[] b, int bH, int cols = 64, double thresh = 16.0)
    {
        if (w <= 0 || accH <= 0 || bH <= 0) return 0;
        int win = Math.Min(accH, bH);                 // 重叠不可能超过任一帧高，也界定搜索窗口
        // 候选重叠 ≤ win 行、容差趟再向外探 1 行，签名只需覆盖 acc 底部 win+1 行
        int aBaseRow = Math.Max(0, accH - win - 1);
        int aRows = accH - aBaseRow;
        var sigA = RowSignatures(acc, w, aRows, cols, aBaseRow);
        var sigB = RowSignatures(b, w, bH, cols, 0);
        int maxCheck = Math.Min(win, 3000);           // 无匹配时最多检查这么多候选，防长图卡顿

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

        // 一趟：从最大候选往下，返回首个 ≥80% 抽样行匹配的重叠；找不到返回 0。
        int Scan(int tol)
        {
            for (int ov = win; ov >= 1; ov--)
            {
                if (win - ov >= maxCheck) break;      // 只检查最接近 win 的 maxCheck 个候选
                int aBase = accH - ov;
                int samples = Math.Min(ov, 32);       // 抽样行数（含首尾），容忍个别离群行
                int good = 0;
                for (int s = 0; s < samples; s++)
                {
                    int r = samples == 1 ? 0 : (int)((long)s * (ov - 1) / (samples - 1)); // 0..ov-1 均匀取点
                    if (RowMatch(aBase + r, r, tol)) good++;
                }
                if (good * 5 >= samples * 4) return ov; // ≥80% 匹配 → 命中最大可靠重叠
            }
            return 0;
        }

        int strict = Scan(0);
        return strict != 0 ? strict : Scan(1);
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
