namespace FrameCapture.Core;

/// <summary>
/// 滚动截图核心拼接算法（R87，纯函数、可单测）。
/// 场景：同一屏幕区域截了 acc（已累积的长图）与 b（滚动后再截的一帧，同宽）。
/// 向下滚动 dy 像素后，b 的顶部若干行与 acc 的底部若干行是同一内容（重叠），b 底部才是新内容。
/// FindOverlap 找重叠行数 ov，Append 把 b 去掉顶部 ov 行后拼到 acc 下方。
/// 行比对用「降采样灰度行签名」：每行取 cols 个等距列的灰度均值，抗轻微色差、够快。
/// 从最大候选重叠往下扫、命中第一个即返回（偏好最大重叠，且滚动量小时命中快）。
/// </summary>
public static class ScrollStitch
{
    /// <summary>每行的降采样灰度签名（长度 h*cols，逐行逐列）。B/G/R 取均值，忽略 A。</summary>
    static byte[] RowSignatures(byte[] bgra, int w, int h, int cols)
    {
        var sig = new byte[h * cols];
        for (int y = 0; y < h; y++)
        {
            int rowOff = y * w * 4, sOff = y * cols;
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
    /// 找 b 顶部与 acc 底部的纵向重叠行数（0..min(accH,bH)）。找不到可靠重叠返回 0。
    /// thresh = 单行平均灰度差阈值（同内容行≈0，错位行≫此值）。
    /// 判据：对每个候选重叠，从最大往下扫，均匀抽样至多 32 行（含首尾），逐行独立比对，
    /// ≥80% 抽样行匹配才算重叠——容忍个别因闪烁光标/抗锯齿而略变的行（旧版「运行均值」早退会误杀，R119 修）。
    /// 两趟匹配（R120 修「裁切/重叠」）：
    ///  ① 严格趟——只比对应行，命中即返回，保证整像素滚动的重叠精确（不多不少一行）。
    ///  ② 容差趟——严格趟全军覆没时才启用，每行允许在 acc 里 ±1 行窗口内找匹配，吸收平滑/分数滚动
    ///     的 ±1px 取整抖动（旧版此时会把整段重叠误判为 0 或过小 → 拼接出现重复内容带「重叠」）。
    ///     容差趟仍从最大往下、≥80% 才收，且 ±1 窗口把高估上界锁在 1 行（肉眼不可见），不会 gross 裁切。
    /// </summary>
    public static int FindOverlap(byte[] acc, int w, int accH, byte[] b, int bH, int cols = 64, double thresh = 16.0)
    {
        if (w <= 0 || accH <= 0 || bH <= 0) return 0;
        int win = Math.Min(accH, bH);                 // 重叠不可能超过任一帧高，也界定搜索窗口
        var sigA = RowSignatures(acc, w, accH, cols);
        var sigB = RowSignatures(b, w, bH, cols);
        int maxCheck = Math.Min(win, 3000);           // 无匹配时最多检查这么多候选，防长图卡顿

        // 单行匹配：tol=0 只比对应行；tol=1 允许 acc 行号 ±1 窗口内任一行达标（吸收 ±1px 抖动）。
        bool RowMatch(int aRow, int bRow, int tol)
        {
            int bi = bRow * cols;
            for (int d = -tol; d <= tol; d++)
            {
                int ar = aRow + d;
                if (ar < 0 || ar >= accH) continue;
                int ai = ar * cols;
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

    /// <summary>把 b 去掉顶部 ov 行后拼到 acc 下方，返回新 BGRA 缓冲（高 accH + bH − ov）。无新内容原样返回。</summary>
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
