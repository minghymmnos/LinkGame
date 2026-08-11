using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 受约束关卡生成器。
/// 给定 LevelConfig（行列/类型数/8 个指标目标值/目标区间），
/// 通过"随机生成 → DifficultyAnalyzer.Analyze → 迭代调优（类型洗牌/位置重排）"生成
/// 尽可能满足目标指标的关卡，最后返回 N 个 LevelInstance。
/// </summary>
public static class LevelGenerator
{
    /// <summary>每个关卡的最大尝试次数（防止死循环）</summary>
    private const int MAX_ATTEMPTS_PER_LEVEL = 300;
    /// <summary>与目标指标允许的最大偏差（L∞ 上界），达到即提前收敛</summary>
    private const float ACCEPTABLE_DEVIATION = 0.18f;
    /// <summary>单个关卡生成最长毫秒数（默认 5000ms = 5 秒）。超时则立即返回当前 best。</summary>
    public const int TIMEOUT_MS_PER_LEVEL = 5000;
    /// <summary>每隔 N 次 attempt 检查一次是否超时（避免 Stopwatch 开销过大）。</summary>
    private const int TIMEOUT_CHECK_INTERVAL = 5;

    /// <summary>
    /// 主入口：根据 LevelConfig 批量生成 count 个关卡。
    /// 异步友好：内部使用 for 循环（可被外层协程包裹），返回生成的关卡列表。
    /// 生成后自动写入 LevelRecordManager 持久化（若无管理器则只返回）。
    /// —— 严格生成 count 个：当单关卡超时/异常/未产生 best 时，按 cfg 给定 rows/cols/typeCount
    ///    随机生成 1 张可配对关卡兜底，并在 remark + generationTimedOut 上标注。
    /// </summary>
    /// <param name="timeoutCount">返回超时或失败兜底为随机关卡的数量</param>
    /// <param name="timeoutMsPerLevel">可选覆写单关卡超时毫秒；-1 则用默认 5000</param>
    public static List<LevelInstance> Generate(LevelConfig cfg, int count, out int timeoutCount, int timeoutMsPerLevel = -1)
    {
        if (cfg == null) cfg = new LevelConfig();
        count = Mathf.Max(1, count);
        int perTimeout = timeoutMsPerLevel > 0 ? timeoutMsPerLevel : TIMEOUT_MS_PER_LEVEL;
        timeoutCount = 0;

        // 合法性校验：行列乘积必须为偶数
        if ((cfg.rows * cfg.cols) % 2 != 0)
        {
            cfg.cols++;
            Debug.LogWarning($"LevelGenerator: rows×cols 不是偶数，已将 cols 调整为 {cfg.cols}");
        }
        if (cfg.typeCount < 1) cfg.typeCount = 2;

        // 从配置解析每个指标的目标归一化值
        float[] targetNorms = DifficultyAnalyzer.ResolveTargetNorms(cfg, new System.Random(Guid.NewGuid().GetHashCode()));
        float targetDD = DifficultyAnalyzer.PredictOverallDD(targetNorms);

        List<LevelInstance> result = new List<LevelInstance>(count);
        System.Random rng = new System.Random(Guid.NewGuid().GetHashCode());

        for (int i = 0; i < count; i++)
        {
            bool timedOut;
            LevelInstance best = null;
            try
            {
                best = GenerateOne(cfg, targetNorms, targetDD, rng, perTimeout, out timedOut);
            }
            catch (Exception e)
            {
                // 任何异常都当作"超时失败"处理，随后走随机关卡兜底，保证不中断整体批量生成
                Debug.LogError($"[LevelGenerator] GenerateOne 异常：{e}");
                timedOut = true;
                best = null;
            }

            // —— 严格保证产出：超时 / 异常 / 未收敛出 best → 用随机关卡兜底
            if (best == null) timedOut = true;
            if (timedOut || best == null)
            {
                timeoutCount++;
                var fallback = BuildFallbackLevelInstance(cfg, rng);
                fallback.generationTimedOut = true;
                fallback.remark = "超时生成失败，已按给定行列与配对类型数随机生成一个关卡";
                best = fallback;
                Debug.LogWarning($"[LevelGenerator] 第 {i + 1}/{count} 个关卡使用随机关卡兜底：R={cfg.rows}, C={cfg.cols}, T={cfg.typeCount}");
            }
            else
            {
                best.generationTimedOut = false;
                if (string.IsNullOrEmpty(best.remark)) best.remark = string.Empty;
            }

            result.Add(best);
            if (LevelRecordManager.Instance != null)
                LevelRecordManager.Instance.UpsertLevel(best);
        }
        // 严格断言：返回列表长度 == 玩家要求生成数量
        UnityEngine.Debug.Assert(result.Count == count, $"[LevelGenerator] 结果数量 {result.Count} != 要求 {count}，生成器兜底逻辑有漏洞。");
        return result;
    }

    /// <summary>兼容旧签名（无 out 参数）：调用新版并忽略超时计数。</summary>
    public static List<LevelInstance> Generate(LevelConfig cfg, int count)
    {
        int _; return Generate(cfg, count, out _);
    }

    /// <summary>
    /// 生成单个关卡（外部迭代调用专用，用于协程逐关生成 + 显示进度条）：
    /// - 自动解析 cfg 的目标指标（若多次调用之间 cfg 不变，目标值固定；rng 每次新建以保证关卡多样性）
    /// - 内部自带异常捕获 + 随机关卡兜底，保证 100% 返回非空 LevelInstance
    /// - out timedOut=true 表示：超时 / 异常 / 未收敛，最终产出的是随机关卡兜底，玩家 UI 可标注警告色
    /// </summary>
    public static LevelInstance GenerateSingle(LevelConfig cfg, out bool timedOut, int timeoutMsPerLevel = -1)
    {
        if (cfg == null) cfg = new LevelConfig();
        int perTimeout = timeoutMsPerLevel > 0 ? timeoutMsPerLevel : TIMEOUT_MS_PER_LEVEL;
        // 合法性校验（与批量生成保持一致）
        if ((cfg.rows * cfg.cols) % 2 != 0) cfg.cols++;
        if (cfg.typeCount < 1) cfg.typeCount = 2;
        var rng = new System.Random(Guid.NewGuid().GetHashCode());
        float[] targetNorms = DifficultyAnalyzer.ResolveTargetNorms(cfg, rng);
        float targetDD = DifficultyAnalyzer.PredictOverallDD(targetNorms);
        timedOut = false;
        LevelInstance best = null;
        try
        {
            best = GenerateOne(cfg, targetNorms, targetDD, rng, perTimeout, out timedOut);
        }
        catch (Exception e)
        {
            Debug.LogError($"[LevelGenerator] GenerateSingle 异常：{e}");
            timedOut = true;
            best = null;
        }
        if (best == null) timedOut = true;
        if (timedOut || best == null)
        {
            var fallback = BuildFallbackLevelInstance(cfg, rng);
            fallback.generationTimedOut = true;
            fallback.remark = "超时生成失败，已按给定行列与配对类型数随机生成一个关卡";
            Debug.LogWarning($"[LevelGenerator] GenerateSingle 随机关卡兜底：R={cfg.rows}, C={cfg.cols}, T={cfg.typeCount}");
            return fallback;
        }
        best.generationTimedOut = false;
        if (string.IsNullOrEmpty(best.remark)) best.remark = string.Empty;
        return best;
    }

    /// <summary>生成单个关卡：多次尝试 → 返回与目标指标最接近的一次。</summary>
    private static LevelInstance GenerateOne(LevelConfig cfg, float[] targetNorms, float targetDD, System.Random rng, int timeoutMs, out bool timedOut)
    {
        timedOut = false;
        LevelInstance best = null;
        float bestCost = float.MaxValue;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        for (int attempt = 0; attempt < MAX_ATTEMPTS_PER_LEVEL; attempt++)
        {
            // 周期性检查超时（每 TIMEOUT_CHECK_INTERVAL 次 + 首次），避免逐次调用 Stopwatch 开销
            if ((attempt % TIMEOUT_CHECK_INTERVAL) == 0 && sw.ElapsedMilliseconds > timeoutMs)
            {
                timedOut = true;
                Debug.LogWarning(
                    $"[LevelGenerator] 单关卡生成超时（>{timeoutMs}ms，attempt={attempt}，" +
                    $"bestCost={bestCost:F3}）。将使用当前 best 方案，后续可尝试：减少行列数、降低 ACCEPTABLE_DEVIATION 收敛精度、减少 8 指标中 SPD(DW/DF/SB) 权重。");
                break;
            }

            // ---------- 1. 构造候选棋盘 ----------
            List<int> typeList = GenerateTypeList(cfg, rng);
            // 打包成 (rows+2)×(cols+2) 网格数据用于 Analyze
            int?[,] grid = BuildGridForEval(cfg, typeList);

            // ---------- 2. 评估（注意 DifficultyAnalyzer.Analyze 依赖 GridManager.Instance） ----------
            // 为避免耦合场景，我们在独立环境下估算指标：
            // 这里复用 DifficultyAnalyzer 的内部分解步骤，但不依赖单例 GridManager。
            var m = EvaluateOnGrid(grid, cfg.rows, cfg.cols, cfg.typeCount);

            // ---------- 3. 计算 cost = W·|归一值-目标| + |综合 DD - 目标 DD| ----------
            float[] actualNorms = new float[8];
            actualNorms[(int)MetricId.VMD] = m.VMD_norm;
            actualNorms[(int)MetricId.TTE] = m.TTE_norm;
            actualNorms[(int)MetricId.TSD] = m.TSD_norm;
            actualNorms[(int)MetricId.APT] = m.APT_norm;
            actualNorms[(int)MetricId.CPR] = m.CPR_norm;
            actualNorms[(int)MetricId.DW]  = m.DW_norm;
            actualNorms[(int)MetricId.DF]  = m.DF_norm;
            actualNorms[(int)MetricId.SB]  = m.SB_norm;

            float cost = 0f;
            float maxDev = 0f;
            for (int i = 0; i < 8; i++)
            {
                float d = Mathf.Abs(actualNorms[i] - targetNorms[i]);
                cost += d;
                if (d > maxDev) maxDev = d;
            }
            cost += 2f * Mathf.Abs(m.DD - targetDD); // 综合 DD 权重更高

            // ---------- 4. 记录最优 ----------
            if (cost < bestCost)
            {
                bestCost = cost;
                best = new LevelInstance
                {
                    levelId = Guid.NewGuid().ToString("N").Substring(0, 10),
                    createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    config = DeepCopy(cfg),
                    metrics = m,
                    gridSnapshot = GridSnapshotFromTypeList(cfg, typeList),
                };
            }

            // ---------- 5. 收敛则提前退出 ----------
            if (maxDev <= ACCEPTABLE_DEVIATION) break;

            // ---------- 6. 调优：每 10 次为一轮，逐步收紧洗牌强度 ----------
            // 本次实现简化为：每轮全部重新随机（成本低，且在 300 轮内有充分机会找到满意解）
            // 精细调优（交换特定瓦片）可以后续再增强。
        }
        sw.Stop();
        return best;
    }

    /// <summary>生成配对类型列表：长度 == rows*cols，按行优先。</summary>
    private static List<int> GenerateTypeList(LevelConfig cfg, System.Random rng)
    {
        int total = cfg.rows * cfg.cols;
        int pairs = total / 2;
        int numTypes = Mathf.Max(1, cfg.typeCount);

        List<int> list = new List<int>(total);
        for (int i = 0; i < pairs; i++)
        {
            int t = i % numTypes;
            list.Add(t); list.Add(t);
        }
        // Fisher-Yates
        int n = list.Count;
        while (n > 1)
        {
            n--;
            int k = rng.Next(n + 1);
            int v = list[k]; list[k] = list[n]; list[n] = v;
        }
        return list;
    }

    /// <summary>打包成 (rows+2)×(cols+2) 的网格，边界=null。</summary>
    private static int?[,] BuildGridForEval(LevelConfig cfg, List<int> typeList)
    {
        int?[,] g = new int?[cfg.rows + 2, cfg.cols + 2];
        int k = 0;
        for (int r = 1; r <= cfg.rows; r++)
            for (int c = 1; c <= cfg.cols; c++)
                g[r, c] = typeList[k++];
        return g;
    }

    /// <summary>从类型列表生成压缩快照（含边界 null → -1）。</summary>
    private static List<int> GridSnapshotFromTypeList(LevelConfig cfg, List<int> typeList)
    {
        int R = cfg.rows + 2, C = cfg.cols + 2;
        List<int> snap = new List<int>(R * C);
        int?[,] g = BuildGridForEval(cfg, typeList);
        for (int r = 0; r < R; r++)
            for (int c = 0; c < C; c++)
                snap.Add(g[r, c] ?? -1);
        return snap;
    }

    /// <summary>深拷贝配置。</summary>
    private static LevelConfig DeepCopy(LevelConfig src)
    {
        LevelConfig dst = new LevelConfig
        {
            rows = src.rows,
            cols = src.cols,
            typeCount = src.typeCount,
            generateCount = src.generateCount,
        };
        dst.metrics.Clear();
        for (int i = 0; i < src.metrics.Count; i++)
            dst.metrics.Add(new MetricConstraint
            {
                id = src.metrics[i].id,
                useGrade = src.metrics[i].useGrade,
                normalizedValue = src.metrics[i].normalizedValue,
                grade = src.metrics[i].grade,
            });
        return dst;
    }

    // ---------- 不依赖 GridManager.Instance 的独立评估函数 ----------
    // 与 DifficultyAnalyzer.Analyze() 核心逻辑一致，但直接对传入 grid 工作，
    // 避免关卡设计器离线生成时场景还未初始化 GridManager 单例的问题。

    private static DifficultyAnalyzer.DifficultyMetrics EvaluateOnGrid(
        int?[,] grid, int rows, int cols, int typeCountHint)
    {
        var m = new DifficultyAnalyzer.DifficultyMetrics();
        m.totalTiles = rows * cols;

        // 收集类型分组
        Dictionary<int, List<PathFinder.Point>> typeGroups = new Dictionary<int, List<PathFinder.Point>>();
        for (int r = 1; r <= rows; r++)
            for (int c = 1; c <= cols; c++)
                if (grid[r, c] != null)
                {
                    int t = grid[r, c].Value;
                    if (!typeGroups.ContainsKey(t)) typeGroups[t] = new List<PathFinder.Point>();
                    typeGroups[t].Add(new PathFinder.Point(r, c));
                }
        m.numTypes = typeGroups.Count;

        // 总对数
        m.totalPairs = 0;
        foreach (var kvp in typeGroups)
            m.totalPairs += kvp.Value.Count * (kvp.Value.Count - 1) / 2;

        // 遍历所有同类型组合算路径
        int validCount = 0, twoTurnCount = 0, totalTurns = 0;
        foreach (var kvp in typeGroups)
        {
            List<PathFinder.Point> tiles = kvp.Value;
            for (int i = 0; i < tiles.Count; i++)
                for (int j = i + 1; j < tiles.Count; j++)
                {
                    var path = PathFinder.FindPath(grid, rows, cols, tiles[i], tiles[j]);
                    if (path != null)
                    {
                        validCount++;
                        int turns = path.Count - 2;
                        totalTurns += turns;
                        if (turns == 2) twoTurnCount++;
                    }
                }
        }
        m.validPairCount = validCount;

        // M1~M5
        m.VMD = m.totalPairs > 0 ? (float)validCount / m.totalPairs : 0f;
        // 调用 DifficultyAnalyzer 内部私有不便，这里重复实现
        m.TTE = CalcEntropy(typeGroups, m.totalTiles);
        m.TSD = CalcDispersion(typeGroups);
        m.APT = validCount > 0 ? (float)totalTurns / validCount : 0f;
        m.CPR = validCount > 0 ? (float)twoTurnCount / validCount : 0f;

        // M6/M7/M8 模拟
        float avgDW, df, logSB;
        SimulateGame(grid, rows, cols, typeGroups, out avgDW, out df, out logSB);
        m.DW = avgDW; m.DF = df; m.logSB = logSB;

        // 归一化
        m.VMD_norm = 1f - m.VMD;
        float maxEnt = Mathf.Log(Mathf.Max(1, m.numTypes), 2);
        m.TTE_norm = maxEnt > 0 ? m.TTE / maxEnt : 0f;
        float maxDist = rows + cols - 2;
        m.TSD_norm = maxDist > 1 ? Mathf.Clamp01((m.TSD - 1f) / (maxDist - 1f)) : 0f;
        m.APT_norm = Mathf.Clamp01(m.APT / 2f);
        m.CPR_norm = Mathf.Clamp01(m.CPR);
        const float DW_LOW = 2f, DW_MID = 8f, SB_LOW = 5f, SB_MID = 40f;
        m.DW_norm = NormU(m.DW, DW_LOW, DW_MID, Mathf.Max(1, m.totalPairs));
        m.DF_norm = Mathf.Clamp01(m.DF);
        m.SB_norm = NormU(m.logSB, SB_LOW, SB_MID, 100f);

        // 聚合
        const float w1 = 0.40f, w2 = 0.30f, w3 = 0.30f;
        const float w4 = 0.60f, w5 = 0.40f;
        const float w6 = 0.40f, w7 = 0.30f, w8 = 0.30f;
        const float alpha = 0.30f, beta = 0.45f, gamma = 0.25f;
        m.VPD = w1 * m.VMD_norm + w2 * m.TTE_norm + w3 * m.TSD_norm;
        m.PRD = w4 * m.APT_norm + w5 * m.CPR_norm;
        m.SPD = w6 * m.DW_norm + w7 * m.DF_norm + w8 * m.SB_norm;
        m.DD = Mathf.Clamp01(alpha * m.VPD + beta * m.PRD + gamma * m.SPD);

        if (m.DD < 0.20f) m.difficultyLevel = "极易";
        else if (m.DD < 0.40f) m.difficultyLevel = "简单";
        else if (m.DD < 0.60f) m.difficultyLevel = "普通";
        else if (m.DD < 0.80f) m.difficultyLevel = "困难";
        else m.difficultyLevel = "极难";

        return m;
    }

    private static float CalcEntropy(Dictionary<int, List<PathFinder.Point>> groups, int total)
    {
        float e = 0f;
        if (total == 0) return 0f;
        foreach (var kvp in groups)
        {
            float p = (float)kvp.Value.Count / total;
            if (p > 0) e -= p * Mathf.Log(p, 2);
        }
        return e;
    }

    private static float CalcDispersion(Dictionary<int, List<PathFinder.Point>> groups)
    {
        float sum = 0f; int n = 0;
        foreach (var kvp in groups)
        {
            var list = kvp.Value;
            if (list.Count < 2) continue;
            long d = 0; int c = 0;
            for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                {
                    d += Math.Abs(list[i].Row - list[j].Row) + Math.Abs(list[i].Col - list[j].Col);
                    c++;
                }
            if (c > 0) { sum += (float)d / c; n++; }
        }
        return n > 0 ? sum / n : 0f;
    }

    private static float NormU(float v, float low, float mid, float high)
    {
        if (v <= low) return 1f;
        if (v <= mid) return 1f - (v - low) / (mid - low);
        if (high <= mid) return 0f;
        return Mathf.Clamp01((v - mid) / (high - mid));
    }

    // 蒙特卡洛模拟：直接复制 DifficultyAnalyzer 的实现，确保结果一致
    private const int MC = 100;
    private static void SimulateGame(int?[,] grid0, int rows, int cols,
        Dictionary<int, List<PathFinder.Point>> typeGroups0,
        out float avgDW, out float df, out float logSB)
    {
        avgDW = 0f; df = 0f; logSB = 0f;
        float tDW = 0f, tSB = 0f;
        int tDead = 0, tSteps = 0;
        System.Random rng = new System.Random();
        for (int run = 0; run < MC; run++)
        {
            int?[,] g = Copy(grid0);
            int sDW = 0, sCount = 0, sDead = 0;
            float sSB = 0f;
            while (true)
            {
                var pairs = FindPairs(g, rows, cols);
                int vpc = pairs.Count;
                if (vpc == 0)
                {
                    bool remain = false;
                    for (int r = 1; r <= rows; r++)
                        for (int c = 1; c <= cols; c++)
                            if (g[r, c] != null) { remain = true; break; }
                    if (!remain) break;
                    sDead++; Shuffle(g, rows, cols, rng);
                    continue;
                }
                sDW += vpc; sCount++;
                sSB += Mathf.Log(vpc, 10);
                int pick = rng.Next(0, vpc);
                var p = pairs[pick];
                g[p.Item1.Row, p.Item1.Col] = null;
                g[p.Item2.Row, p.Item2.Col] = null;
            }
            tDW += sCount > 0 ? (float)sDW / sCount : 0f;
            tDead += sDead;
            tSteps += sCount;
            tSB += sSB;
        }
        avgDW = tDW / MC;
        df = tSteps > 0 ? (float)tDead / tSteps : 0f;
        logSB = tSB / MC;
    }

    private static List<Tuple<PathFinder.Point, PathFinder.Point>> FindPairs(int?[,] g, int rows, int cols)
    {
        var result = new List<Tuple<PathFinder.Point, PathFinder.Point>>();
        Dictionary<int, List<PathFinder.Point>> groups = new Dictionary<int, List<PathFinder.Point>>();
        for (int r = 1; r <= rows; r++)
            for (int c = 1; c <= cols; c++)
                if (g[r, c] != null)
                {
                    int t = g[r, c].Value;
                    if (!groups.ContainsKey(t)) groups[t] = new List<PathFinder.Point>();
                    groups[t].Add(new PathFinder.Point(r, c));
                }
        foreach (var kvp in groups)
        {
            var list = kvp.Value;
            for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                {
                    var path = PathFinder.FindPath(g, rows, cols, list[i], list[j]);
                    if (path != null)
                        result.Add(Tuple.Create(list[i], list[j]));
                }
        }
        return result;
    }

    private static void Shuffle(int?[,] g, int rows, int cols, System.Random rng)
    {
        List<int> ts = new List<int>();
        List<(int, int)> ps = new List<(int, int)>();
        for (int r = 1; r <= rows; r++)
            for (int c = 1; c <= cols; c++)
                if (g[r, c] != null)
                {
                    ts.Add(g[r, c].Value); ps.Add((r, c));
                }
        int n = ts.Count;
        while (n > 1) { n--; int k = rng.Next(n + 1); int v = ts[k]; ts[k] = ts[n]; ts[n] = v; }
        for (int i = 0; i < ps.Count; i++) g[ps[i].Item1, ps[i].Item2] = ts[i];
    }

    private static int?[,] Copy(int?[,] s)
    {
        int R = s.GetLength(0), C = s.GetLength(1);
        int?[,] d = new int?[R, C];
        for (int r = 0; r < R; r++)
            for (int c = 0; c < C; c++) d[r, c] = s[r, c];
        return d;
    }

    /// <summary>
    /// 兜底随机关卡构造：直接按 cfg.rows/cols/typeCount 生成一张可配对的棋盘。
    /// 保证 total = rows*cols 为偶数（已在 Generate 内做过 cols++ 修正），
    /// 并按每对相同 type 的方式填列 → Fisher-Yates 打乱 → 写入 gridSnapshot。
    /// 同时给出一份「最小指标估算」——即使指标不达标也能确保关卡能跳转与配对消除。
    /// </summary>
    private static LevelInstance BuildFallbackLevelInstance(LevelConfig cfg, System.Random rng)
    {
        List<int> typeList = GenerateTypeList(cfg, rng);
        var grid = BuildGridForEval(cfg, typeList);
        var m = new DifficultyAnalyzer.DifficultyMetrics();
        try { m = EvaluateOnGrid(grid, cfg.rows, cfg.cols, cfg.typeCount); }
        catch (Exception e) { Debug.LogWarning($"[LevelGenerator] BuildFallbackLevelInstance 评估兜底指标异常，使用默认 DD=0.5：{e.Message}"); }

        var lv = new LevelInstance
        {
            levelId = Guid.NewGuid().ToString("N").Substring(0, 10),
            createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            config = DeepCopy(cfg),
            metrics = m,
            gridSnapshot = GridSnapshotFromTypeList(cfg, typeList),
        };
        if (lv.metrics != null && lv.metrics.DD <= 0f)
            lv.overallDD = 0.5f;   // 兜底 DD 中间值，避免 UI 显示为"0.000 极易"看起来像误报
        return lv;
    }
}
