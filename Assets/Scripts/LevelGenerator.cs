using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// 受约束关卡生成器。
/// 给定 LevelConfig（行列/类型数/8 个指标目标值/目标区间），
/// 通过"随机生成 → DifficultyAnalyzer.Analyze → 迭代调优（类型洗牌/位置重排）"生成
/// 尽可能满足目标指标的关卡，最后返回 N 个 LevelInstance。
/// </summary>
public static class LevelGenerator
{
    /// <summary>与目标指标允许的最大偏差（L∞ 上界），达到即提前收敛</summary>
    private const float ACCEPTABLE_DEVIATION = 0.18f;
    /// <summary>单个关卡生成最长毫秒数（默认 5000ms = 5 秒）。超时则立即返回当前 best。</summary>
    public const int TIMEOUT_MS_PER_LEVEL = 5000;
    /// <summary>每隔 N 次 attempt 检查一次是否超时（避免 Stopwatch 开销过大）。</summary>
    private const int TIMEOUT_CHECK_INTERVAL = 5;

    // ---------- 方案3（两阶段搜索）参数 ----------
    /// <summary>阶段一保留的最优静态候选数（阶段二精评数量上限）</summary>
    private const int TOP_K = 12;
    /// <summary>阶段一静态筛选的最大尝试次数（实际由超时预算控制，静态评估比全量快约百倍）</summary>
    private const int PHASE1_MAX_ATTEMPTS = 1500;
    /// <summary>全量蒙特卡洛模拟次数（默认评估 / 最终复核）</summary>
    private const int FULL_MC = 100;
    /// <summary>阶段二搜索期蒙特卡洛降采样次数（先粗评 top-K，再对最优复核）</summary>
    private const int SEARCH_MC = 30;

    // ---------- 方案2（定向调优）参数 ----------
    /// <summary>阶段一爬山停滞上限：连续未改进达到该次数后重启/换起点（多起点策略）</summary>
    private const int STAGNATION_LIMIT = 40;
    /// <summary>阶段二精调的最大变异尝试次数（每次做完整评估，开销较高故限制次数）</summary>
    private const int PHASE2_HILL_ATTEMPTS = 8;

    /// <summary>
    /// 主入口：根据 LevelConfig 批量生成 count 个关卡。
    /// 异步友好：内部使用 for 循环（可被外层协程包裹），返回生成的关卡列表。
    /// 生成后自动写入 LevelRecordManager 持久化（若无管理器则只返回）。
    /// —— 严格生成 count 个。兜底策略（方案1）：
    ///    只有"连一个候选都没产出"（异常）时才随机生成 1 张可配对关卡兜底并标注失败；
    ///    超时 / 未完全命中目标指标等级时保留最接近目标的 best 并标注偏差，不再换成随机关卡。
    /// </summary>
    /// <param name="timeoutCount">返回超时或失败兜底（非完全达标）的数量</param>
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
            float bestDev = 0f;
            LevelInstance best = null;
            try
            {
                best = GenerateOne(cfg, targetNorms, targetDD, rng, perTimeout, out timedOut, out bestDev);
            }
            catch (Exception e)
            {
                // 任何异常都当作"生成失败"处理，随后走随机关卡兜底，保证不中断整体批量生成
                Debug.LogError($"[LevelGenerator] GenerateOne 异常：{e}");
                timedOut = true;
                best = null;
            }

            // —— 方案1 兜底策略：
            //    1) 真失败（连一个候选都没产出）→ 随机关卡兜底并标注失败；
            //    2) 超时 / 未完全达标但已有近似最优解 → 保留 best 并标注偏差，
            //       不再"丢弃 best 换成随机关卡"（旧逻辑导致绝大多数关卡都是随便生成的）。
            if (best == null)
            {
                timeoutCount++;
                var fallback = BuildFallbackLevelInstance(cfg, rng);
                fallback.generationTimedOut = true;
                fallback.remark = "生成失败，已按给定行列与配对类型数随机生成一个关卡";
                best = fallback;
                Debug.LogWarning($"[LevelGenerator] 第 {i + 1}/{count} 个关卡生成失败，使用随机关卡兜底：R={cfg.rows}, C={cfg.cols}, T={cfg.typeCount}");
            }
            else
            {
                best.generationTimedOut = timedOut;
                if (timedOut)
                {
                    timeoutCount++;
                    best.remark = bestDev > 0.001f
                        ? $"生成超时，已返回最接近目标的关卡（未完全命中目标指标等级，最大偏差 {bestDev:F2}）"
                        : "生成超时，已返回最接近目标的关卡（指标已落入目标等级）";
                    Debug.LogWarning($"[LevelGenerator] 第 {i + 1}/{count} 个关卡超时（指标最大偏差 {bestDev:F2}），保留近似最优而非随机关卡。");
                }
                else
                {
                    // 300 次尝试用尽但未超时：也可能未完全落入目标等级，同样如实标注
                    best.remark = bestDev > 0.001f
                        ? $"未完全命中目标指标等级（最大偏差 {bestDev:F2}），已返回最接近目标的关卡"
                        : string.Empty;
                }
            }

            // 详细指标日志：目标 8 指标/DD/难度等级 → 实际值 → 偏差（达标 Log，超时/未达标 LogWarning）
            LogMetricReport(targetNorms, targetDD, best, best.generationTimedOut || bestDev > 0.001f);

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
    /// - 内部自带异常捕获 + 兜底，保证 100% 返回非空 LevelInstance
    /// - out timedOut=true 表示：超时（或异常），返回的是最接近目标的 best（并已在 remark 标注偏差）；
    ///   仅当连候选都没有时才随机兜底（remark 标注失败），玩家 UI 可据此显示警告色
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
        float bestDev = 0f;
        try
        {
            best = GenerateOne(cfg, targetNorms, targetDD, rng, perTimeout, out timedOut, out bestDev);
        }
        catch (Exception e)
        {
            Debug.LogError($"[LevelGenerator] GenerateSingle 异常：{e}");
            timedOut = true;
            best = null;
        }
        // —— 方案1：仅当完全没有任何候选时才随机关卡兜底；
        //    否则超时/未达标都保留近似最优 best，并如实标注偏差（不再丢弃 best 换成随机盘面）。
        if (best == null)
        {
            var fallback = BuildFallbackLevelInstance(cfg, rng);
            fallback.generationTimedOut = true;
            fallback.remark = "生成失败，已按给定行列与配对类型数随机生成一个关卡";
            Debug.LogWarning($"[LevelGenerator] GenerateSingle 生成失败，使用随机关卡兜底：R={cfg.rows}, C={cfg.cols}, T={cfg.typeCount}");
            LogMetricReport(targetNorms, targetDD, fallback, true);
            return fallback;
        }
        best.generationTimedOut = timedOut;
        if (timedOut)
        {
            best.remark = bestDev > 0.001f
                ? $"生成超时，已返回最接近目标的关卡（未完全命中目标指标等级，最大偏差 {bestDev:F2}）"
                : "生成超时，已返回最接近目标的关卡（指标已落入目标等级）";
            Debug.LogWarning($"[LevelGenerator] GenerateSingle 超时（指标最大偏差 {bestDev:F2}），保留近似最优而非随机关卡。");
        }
        else
        {
            best.remark = bestDev > 0.001f
                ? $"未完全命中目标指标等级（最大偏差 {bestDev:F2}），已返回最接近目标的关卡"
                : string.Empty;
        }
        // 详细指标日志：目标 8 指标/DD/难度等级 → 实际值 → 偏差（达标 Log，超时/未达标 LogWarning）
        LogMetricReport(targetNorms, targetDD, best, best.generationTimedOut || bestDev > 0.001f);
        return best;
    }

    /// <summary>
    /// 生成单个关卡：两阶段搜索（方案3）。
    /// 阶段一（快速筛选）：只评估静态指标 M1~M5（不跑蒙特卡洛），
    ///   在相同时间预算下把候选尝试次数提升约百倍，保留 top-K 最佳静态候选；
    /// 阶段二（精评）：对 top-K 跑蒙特卡洛（降采样 SEARCH_MC 次）选出全局最优，
    ///   最后对最优候选用 FULL_MC 次精确复核。
    /// 收敛判据（方案1）：只要求"玩家显式设置过"的指标落入其目标等级区间即达标；
    /// 若 8 个指标全部未显式设置（全默认），则保持旧精确判据（8 指标同时偏差 ≤ 0.18）。
    /// </summary>
    /// <param name="bestDev">best 对应的"达标偏差"（显式指标的区间外距离；无显式指标时为全量数值偏差最大值），用于超时/未达标报告</param>
    private static LevelInstance GenerateOne(LevelConfig cfg, float[] targetNorms, float targetDD, System.Random rng, int timeoutMs, out bool timedOut, out float bestDev)
    {
        timedOut = false;
        bestDev = 0f;
        LevelInstance best = null;
        float bestCost = float.MaxValue;
        // 玩家显式设置的指标集合：只有这些指标参与收敛判据与 cost 引导
        bool[] explicitFlags;
        bool anyExplicit;
        GetExplicitMetricFlags(cfg, out explicitFlags, out anyExplicit);
        // 是否显式设置了动态指标（M6~M8）：若是，阶段一（无蒙特卡洛）无法判断其达标情况，必须跑满预算后靠阶段二精评
        bool hasDynamicExplicit = explicitFlags[5] || explicitFlags[6] || explicitFlags[7];
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // ================================================================
        // 阶段一：快速筛选 + 定向调优（方案2：多起点局部爬山）
        //   - 探索：全局随机采样（GenerateTypeList）保证覆盖面
        //   - 定向调优：从当前起点做定向变异（指标分治：TTE 用分布迁移，其余用位置交换），
        //     cost 下降则接受为新起点（爬山）
        //   - 多起点：连续 STAGNATION_LIMIT 次无改进 → 从精英池换起点，或重新全局探索
        //   - 精英池：始终保留静态最优 TOP_K 个候选，供阶段二精评（防止陷入局部最优）
        // ================================================================
        List<Candidate> elite = new List<Candidate>(TOP_K + 1);
        List<int> hillStart = null;              // 当前爬山起点
        float hillStartCost = float.MaxValue;
        float[] hillStartNorms = null;
        int stagnation = 0;                      // 连续未改进次数
        bool exploring = true;                   // 是否需要重新全局采样

        for (int attempt = 0; attempt < PHASE1_MAX_ATTEMPTS; attempt++)
        {
            if ((attempt % TIMEOUT_CHECK_INTERVAL) == 0 && sw.ElapsedMilliseconds > timeoutMs)
            {
                timedOut = true;
                Debug.LogWarning($"[LevelGenerator] 阶段一（静态筛选 + 定向调优）超时（>{timeoutMs}ms，attempt={attempt}，精英池={elite.Count}）。进入阶段二精评已有候选。");
                break;
            }

            // 1) 生成候选：全局探索 or 定向变异（爬山）
            List<int> typeList = (exploring || hillStart == null)
                ? GenerateTypeList(cfg, rng)
                : DirectedMutate(hillStart, cfg, hillStartNorms, targetNorms, explicitFlags, rng);
            exploring = false;

            // 2) 静态评估（仅 M1~M5，不跑蒙特卡洛）
            int?[,] grid = BuildGridForEval(cfg, typeList);
            var m = EvaluateStaticOnGrid(grid, cfg.rows, cfg.cols, cfg.typeCount);
            float[] actualNorms = NormsFromMetrics(m);

            // 3) 静态 cost：显式静态指标达标偏差（硬约束）+ 静态指标数值偏差（软梯度，持续引导逼近目标）
            float cost = 0f;
            float staticDev = 0f;
            for (int i = 0; i < 5; i++)
            {
                if (explicitFlags[i])
                {
                    float dv = MetricDev(actualNorms[i], cfg.metrics[i]);
                    cost += dv;
                    if (dv > staticDev) staticDev = dv;
                }
                cost += 0.5f * Mathf.Abs(actualNorms[i] - targetNorms[i]);
            }

            // 4) 精英池维护（保留静态最优 TOP_K）
            InsertElite(elite, cost, typeList, actualNorms);

            // 5) 无显式动态指标时：静态显式指标全部落入等级区间即可提前进入阶段二精评
            if (!hasDynamicExplicit && staticDev <= 0.001f) break;

            // 6) 爬山接受准则：cost 不升则接受为新起点（允许等值接受，避免过早停滞）
            if (hillStart == null || cost <= hillStartCost)
            {
                hillStart = typeList;
                hillStartCost = cost;
                hillStartNorms = actualNorms;
                stagnation = 0;
            }
            else
            {
                stagnation++;
            }

            // 7) 停滞处理：一半概率从精英池换起点（多起点），一半概率重新全局探索
            if (stagnation >= STAGNATION_LIMIT)
            {
                stagnation = 0;
                if (elite.Count > 0 && rng.NextDouble() < 0.5)
                {
                    Candidate seed = elite[rng.Next(elite.Count)];
                    hillStart = seed.typeList;
                    hillStartCost = seed.cost;
                    hillStartNorms = seed.norms;
                }
                else
                {
                    hillStart = null;
                    exploring = true;
                }
            }
        }

        // 阶段一没有任何候选（超时过早）→ 返回 null，由外层随机兜底
        if (elite.Count == 0) { sw.Stop(); return null; }

        // ================================================================
        // 阶段二：精评精英池 —— 跑蒙特卡洛（降采样 SEARCH_MC 次），按完整 cost 选出全局最优
        // ================================================================
        List<int> bestTypeList = null;
        for (int k = 0; k < elite.Count; k++)
        {
            List<int> tl = elite[k].typeList;
            int?[,] grid = BuildGridForEval(cfg, tl);
            var m = EvaluateOnGrid(grid, cfg.rows, cfg.cols, cfg.typeCount, SEARCH_MC);
            float[] actualNorms = NormsFromMetrics(m);
            float cost, maxDev, explicitDev;
            cost = ComputeCost(actualNorms, targetNorms, targetDD, m.DD, cfg, explicitFlags, out maxDev, out explicitDev);
            if (cost < bestCost)
            {
                bestCost = cost;
                bestDev = anyExplicit ? explicitDev : maxDev;
                bestTypeList = tl;
                best = new LevelInstance
                {
                    levelId = Guid.NewGuid().ToString("N").Substring(0, 10),
                    createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    config = DeepCopy(cfg),
                    metrics = m,
                    gridSnapshot = GridSnapshotFromTypeList(cfg, tl),
                };
            }
            // 阶段二内收敛：显式指标全部落入等级区间（全默认时 8 指标偏差 ≤ 阈值）
            if (anyExplicit ? explicitDev <= 0.001f : maxDev <= ACCEPTABLE_DEVIATION) break;
            // 已精评完一个候选后再检查超时：保证至少完成一个候选的精评，避免返回 null
            if (((k + 1) % 3) == 0 && sw.ElapsedMilliseconds > timeoutMs)
            {
                timedOut = true;
                Debug.LogWarning($"[LevelGenerator] 阶段二（精评）超时，已精评 {k + 1}/{elite.Count} 个候选，取当前最优。");
                break;
            }
        }

        // ================================================================
        // 阶段二局部精调（方案2）：对已选最优做少量"完整评估"的定向变异爬山，
        // 继续在动态指标（M6~M8）与综合 DD 上下降（搜索期降采样选优，这里精评改进）
        // ================================================================
        if (best != null && bestTypeList != null)
        {
            List<int> curList = bestTypeList;
            float curCost = bestCost;
            float[] curNorms = NormsFromMetrics(best.metrics);
            for (int it = 0; it < PHASE2_HILL_ATTEMPTS; it++)
            {
                // 精调属于"额外优化"：超时只停止优化，不改写 timedOut（避免把已达标关卡误标为超时）
                if (sw.ElapsedMilliseconds > timeoutMs) break;
                List<int> cand = DirectedMutate(curList, cfg, curNorms, targetNorms, explicitFlags, rng);
                int?[,] g = BuildGridForEval(cfg, cand);
                var m2 = EvaluateOnGrid(g, cfg.rows, cfg.cols, cfg.typeCount, SEARCH_MC);
                float[] n2 = NormsFromMetrics(m2);
                float cost2, maxDev2, explicitDev2;
                cost2 = ComputeCost(n2, targetNorms, targetDD, m2.DD, cfg, explicitFlags, out maxDev2, out explicitDev2);
                if (cost2 < curCost)
                {
                    curCost = cost2; curList = cand; curNorms = n2;
                    bestCost = cost2;
                    bestDev = anyExplicit ? explicitDev2 : maxDev2;
                    bestTypeList = cand;
                    best.metrics = m2;
                    best.gridSnapshot = GridSnapshotFromTypeList(cfg, cand);
                }
            }
        }

        // ================================================================
        // 最终复核：对最优候选用 FULL_MC 次蒙特卡洛精确评估，
        // 避免搜索期降采样带来的随机波动，保证存档/展示的指标可信。
        // ================================================================
        if (best != null && bestTypeList != null)
        {
            int?[,] grid = BuildGridForEval(cfg, bestTypeList);
            var mFull = EvaluateOnGrid(grid, cfg.rows, cfg.cols, cfg.typeCount, FULL_MC);
            best.metrics = mFull;
            float[] actualNorms = NormsFromMetrics(mFull);
            float cost, maxDev, explicitDev;
            ComputeCost(actualNorms, targetNorms, targetDD, mFull.DD, cfg, explicitFlags, out maxDev, out explicitDev);
            bestDev = anyExplicit ? explicitDev : maxDev;
        }
        sw.Stop();
        return best;
    }

    /// <summary>阶段一精英候选（方案2）：静态 cost + 类型列表 + 静态指标归一值快照。</summary>
    private sealed class Candidate
    {
        public float cost;
        public List<int> typeList;
        public float[] norms;
    }

    /// <summary>
    /// 精英池维护（方案2：保留前几名防止陷入局部最优）：
    /// 按 cost 升序保留最优 TOP_K 个候选，同时作为阶段二精评队列与爬山换起点来源。
    /// </summary>
    private static void InsertElite(List<Candidate> elite, float cost, List<int> typeList, float[] norms)
    {
        if (elite.Count >= TOP_K && cost >= elite[elite.Count - 1].cost) return; // 不如池内最差，直接丢弃
        elite.Add(new Candidate { cost = cost, typeList = typeList, norms = norms });
        elite.Sort((a, b) => a.cost.CompareTo(b.cost));
        if (elite.Count > TOP_K) elite.RemoveAt(elite.Count - 1);
    }

    /// <summary>
    /// 计算"玩家显式设置过"的指标集合（方案1 收敛范围）：
    /// - useGrade=true（按分级）或 normalizedValue 偏离默认值 0.5 → 视为显式设置；
    /// - 若 8 个指标全部未显式设置（全默认面板），则退化为全量参与，保持旧行为。
    /// </summary>
    private static void GetExplicitMetricFlags(LevelConfig cfg, out bool[] flags, out bool anyExplicit)
    {
        flags = new bool[8];
        anyExplicit = false;
        if (cfg == null || cfg.metrics == null || cfg.metrics.Count != 8) return;
        for (int i = 0; i < 8; i++)
        {
            var mc = cfg.metrics[i];
            flags[i] = mc != null && (mc.useGrade || Mathf.Abs(mc.normalizedValue - 0.5f) > 0.001f);
            if (flags[i]) anyExplicit = true;
        }
        if (!anyExplicit)
        {
            for (int i = 0; i < 8; i++) flags[i] = true;
        }
    }

    /// <summary>
    /// 计算单个指标相对其约束的"达标偏差"：
    /// - useGrade=true：实际值到目标等级区间的距离（区间内=0，即达标）；
    /// - useGrade=false：实际值到精确目标值的绝对偏差。
    /// </summary>
    private static float MetricDev(float actualNorm, MetricConstraint c)
    {
        if (c == null) return Mathf.Abs(actualNorm - 0.5f);
        if (c.useGrade)
        {
            int g = (int)c.grade;
            float lo = DifficultyGradeUtil.GradeLower[g];
            float hi = (g == 4) ? 1.0f : DifficultyGradeUtil.GradeUpper[g];
            if (actualNorm < lo) return lo - actualNorm;
            if (actualNorm >= hi) return actualNorm - hi;
            return 0f;
        }
        return Mathf.Abs(actualNorm - c.normalizedValue);
    }

    /// <summary>从 DifficultyMetrics 提取 8 个指标的归一化值数组。</summary>
    private static float[] NormsFromMetrics(DifficultyAnalyzer.DifficultyMetrics m)
    {
        return new float[8]
        {
            m.VMD_norm, m.TTE_norm, m.TSD_norm, m.APT_norm,
            m.CPR_norm, m.DW_norm, m.DF_norm, m.SB_norm,
        };
    }

    /// <summary>
    /// 计算完整 cost = Σ(显式指标达标偏差) + 2·|综合 DD - 目标 DD|，
    /// 同时输出 maxDev（全量数值偏差最大值）与 explicitDev（显式指标达标偏差最大值）。
    /// </summary>
    private static float ComputeCost(float[] actualNorms, float[] targetNorms, float targetDD, float actualDD,
        LevelConfig cfg, bool[] explicitFlags, out float maxDev, out float explicitDev)
    {
        float cost = 0f;
        maxDev = 0f;
        explicitDev = 0f;
        for (int i = 0; i < 8; i++)
        {
            float d = Mathf.Abs(actualNorms[i] - targetNorms[i]);
            if (d > maxDev) maxDev = d;
            if (explicitFlags[i])
            {
                MetricConstraint mc = (cfg != null && cfg.metrics != null && cfg.metrics.Count == 8) ? cfg.metrics[i] : null;
                float dv = MetricDev(actualNorms[i], mc);
                cost += dv;
                if (dv > explicitDev) explicitDev = dv;
            }
        }
        cost += 2f * Mathf.Abs(actualDD - targetDD); // 综合 DD 权重更高
        return cost;
    }

    /// <summary>
    /// 输出关卡指标日志（目标 → 实际 → 偏差）。达标用 Log，超时/未达标/失败用 LogWarning。
    /// </summary>
    private static void LogMetricReport(float[] targetNorms, float targetDD, LevelInstance lv, bool warn)
    {
        string report = FormatMetricReport(targetNorms, targetDD, lv);
        if (warn) Debug.LogWarning("[LevelGenerator] " + report);
        else Debug.Log("[LevelGenerator] " + report);
    }

    /// <summary>
    /// 生成"指标生成报告"多行文本：目标 8 指标归一值 + 目标 DD/难度等级，
    /// 实际 8 指标归一值 + 实际 DD/难度等级，以及每个指标与目标值的偏差。
    /// 供生成关卡时的控制台详细日志使用。
    /// </summary>
    private static string FormatMetricReport(float[] targetNorms, float targetDD, LevelInstance lv)
    {
        if (lv == null || lv.metrics == null) return "（无指标数据可报告）";
        var m = lv.metrics;
        float[] actuals = new float[8]
        {
            m.VMD_norm, m.TTE_norm, m.TSD_norm, m.APT_norm,
            m.CPR_norm, m.DW_norm, m.DF_norm, m.SB_norm,
        };
        var sb = new StringBuilder();
        sb.AppendLine($"关卡 {lv.levelId}  指标生成报告（目标 → 实际，偏差 = 实际 - 目标）：");
        sb.AppendLine($"  综合 DD：目标 {targetDD:F3} ({DifficultyGradeUtil.ToName(DifficultyAnalyzer.NormalizedValueToGrade(targetDD))})" +
                      $" → 实际 {m.DD:F3} ({m.difficultyLevel})，偏差 {FmtDev(m.DD - targetDD)}");
        float maxDev = 0f;
        for (int i = 0; i < 8; i++)
        {
            float d = (i < actuals.Length && i < targetNorms.Length) ? actuals[i] - targetNorms[i] : 0f;
            if (Mathf.Abs(d) > maxDev) maxDev = Mathf.Abs(d);
            string name = (i < DifficultyAnalyzer.MetricLabels.Length) ? DifficultyAnalyzer.MetricLabels[i] : $"M{i + 1}";
            float tgt = (i < targetNorms.Length) ? targetNorms[i] : float.NaN;
            float act = (i < actuals.Length) ? actuals[i] : float.NaN;
            sb.AppendLine($"  {name}：目标 {tgt:F3} → 实际 {act:F3}，偏差 {FmtDev(d)}");
        }
        sb.AppendLine($"  最大指标偏差 {FmtDev(maxDev)}  生成状态：{(lv.generationTimedOut ? "超时/未完全达标" : "达标")}" +
                      (string.IsNullOrEmpty(lv.remark) ? "" : $"  （{lv.remark}）"));
        return sb.ToString();
    }

    /// <summary>带符号的三位小数偏差格式化，如 +0.035 / -0.120。</summary>
    private static string FmtDev(float d)
    {
        return (d >= 0f ? "+" : "-") + Mathf.Abs(d).ToString("F3");
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

    // ================================================================
    // 方案2：定向变异算子（局部爬山 / 指标分治调优使用）
    // ================================================================

    /// <summary>按计数数组计算香农熵（bit）。</summary>
    private static float EntropyFromCounts(int[] counts, int total)
    {
        if (total <= 0) return 0f;
        float h = 0f;
        for (int i = 0; i < counts.Length; i++)
        {
            if (counts[i] <= 0) continue;
            float p = (float)counts[i] / total;
            h -= p * Mathf.Log(p, 2);
        }
        return h;
    }

    /// <summary>
    /// 变异算子 A（位置交换）：随机交换类型列表中的两个格子，等价于交换棋盘两格内容。
    /// 不改变类型分布（TTE 不变），主要影响 TSD / VMD / APT / CPR 等布局类指标。
    /// </summary>
    private static List<int> MutateSwap(List<int> src, System.Random rng)
    {
        List<int> list = new List<int>(src);
        int n = list.Count;
        if (n < 2) return list;
        int a = rng.Next(n);
        int b = rng.Next(n);
        for (int t = 0; t < 8 && list[a] == list[b]; t++) b = rng.Next(n); // 尽量让两格类型不同
        if (list[a] == list[b]) return list; // 交换同类型无意义
        int tmp = list[a]; list[a] = list[b]; list[b] = tmp;
        return list;
    }

    /// <summary>
    /// 变异算子 B（分布迁移）：把"一对"瓦片从稀有类型迁移到常见类型（或反向），
    /// 用于调节 M2 图标类型熵 TTE——目标熵更低则加剧集中，更高则趋向均匀。
    /// 迁移后保持总数不变，且不让任何类型消失（源类型迁移后至少保留 1 对）。
    /// </summary>
    private static List<int> MutateDistribution(List<int> src, LevelConfig cfg, float targetTTEnorm, System.Random rng)
    {
        List<int> list = new List<int>(src);
        int numTypes = Mathf.Max(1, cfg.typeCount);
        if (numTypes < 2) return list;
        int[] counts = new int[numTypes];
        for (int i = 0; i < list.Count; i++)
        {
            int t = list[i];
            if (t >= 0 && t < numTypes) counts[t]++;
        }

        float maxEnt = Mathf.Log(numTypes, 2);
        float curNorm = maxEnt > 0 ? EntropyFromCounts(counts, list.Count) / maxEnt : 0f;

        // 找对数最多 / 最少的类型
        int maxT = 0, minT = 0;
        for (int t = 1; t < numTypes; t++)
        {
            if (counts[t] > counts[maxT]) maxT = t;
            if (counts[t] < counts[minT]) minT = t;
        }

        // 目标更不均匀 → 稀有→常见（加剧集中，熵降低）；目标更均匀 → 常见→稀有（熵升高）
        bool wantLower = targetTTEnorm < curNorm;
        int fromT = wantLower ? minT : maxT;
        int toT = wantLower ? maxT : minT;
        if (fromT == toT) return list;
        if (counts[fromT] <= 2) return list; // 迁移后该类型将消失，放弃本次变异

        // 迁移一对（2 个瓦片）
        int need = 2;
        for (int i = 0; i < list.Count && need > 0; i++)
        {
            if (list[i] == fromT) { list[i] = toT; need--; }
        }
        return list;
    }

    /// <summary>
    /// 定向变异（方案2 指标分治）：挑出静态指标 M1~M5 中偏差最大者，
    /// TTE 偏差大 → 用分布迁移算子 B；其余（VMD/TSD/APT/CPR）偏差大 → 用位置交换算子 A。
    /// </summary>
    private static List<int> DirectedMutate(List<int> src, LevelConfig cfg,
        float[] currentNorms, float[] targetNorms, bool[] explicitFlags, System.Random rng)
    {
        int worst = (int)MetricId.TSD;
        float worstDev = -1f;
        for (int i = 0; i <= (int)MetricId.CPR; i++) // M1~M5
        {
            MetricConstraint mc = (cfg != null && cfg.metrics != null && cfg.metrics.Count == 8) ? cfg.metrics[i] : null;
            float dev = (explicitFlags[i] && mc != null)
                ? MetricDev(currentNorms[i], mc)
                : Mathf.Abs(currentNorms[i] - targetNorms[i]);
            if (dev > worstDev) { worstDev = dev; worst = i; }
        }

        if (worst == (int)MetricId.TTE)
            return MutateDistribution(src, cfg, targetNorms[(int)MetricId.TTE], rng);
        return MutateSwap(src, rng);
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
    //
    // 方案3：把评估拆成"静态部分（M1~M5，无蒙特卡洛）"与"全量部分（含 M6~M8）"。
    // 阶段一用静态部分做百倍速筛选，阶段二才跑蒙特卡洛，大幅提升单位时间内的候选尝试次数。

    /// <summary>收集网格内所有瓦片按类型分组。</summary>
    private static Dictionary<int, List<PathFinder.Point>> CollectTypeGroups(int?[,] grid, int rows, int cols)
    {
        Dictionary<int, List<PathFinder.Point>> typeGroups = new Dictionary<int, List<PathFinder.Point>>();
        for (int r = 1; r <= rows; r++)
            for (int c = 1; c <= cols; c++)
                if (grid[r, c] != null)
                {
                    int t = grid[r, c].Value;
                    if (!typeGroups.ContainsKey(t)) typeGroups[t] = new List<PathFinder.Point>();
                    typeGroups[t].Add(new PathFinder.Point(r, c));
                }
        return typeGroups;
    }

    /// <summary>
    /// 只评估静态指标（M1~M5 + 统计信息 + VPD/PRD 聚合），不跑蒙特卡洛。
    /// 用于阶段一快速筛选，速度约为全量评估的百倍（省去了 100 次整局模拟）。
    /// </summary>
    private static DifficultyAnalyzer.DifficultyMetrics EvaluateStaticOnGrid(int?[,] grid, int rows, int cols, int typeCountHint)
    {
        var m = new DifficultyAnalyzer.DifficultyMetrics();
        m.totalTiles = rows * cols;

        Dictionary<int, List<PathFinder.Point>> typeGroups = CollectTypeGroups(grid, rows, cols);
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
        m.TTE = CalcEntropy(typeGroups, m.totalTiles);
        m.TSD = CalcDispersion(typeGroups);
        m.APT = validCount > 0 ? (float)totalTurns / validCount : 0f;
        m.CPR = validCount > 0 ? (float)twoTurnCount / validCount : 0f;

        // 归一化 M1~M5
        m.VMD_norm = 1f - m.VMD;
        float maxEnt = Mathf.Log(Mathf.Max(1, m.numTypes), 2);
        m.TTE_norm = maxEnt > 0 ? m.TTE / maxEnt : 0f;
        float maxDist = rows + cols - 2;
        m.TSD_norm = maxDist > 1 ? Mathf.Clamp01((m.TSD - 1f) / (maxDist - 1f)) : 0f;
        m.APT_norm = Mathf.Clamp01(m.APT / 2f);
        m.CPR_norm = Mathf.Clamp01(m.CPR);

        // 维度聚合（VPD / PRD，静态可算）
        const float w1 = 0.40f, w2 = 0.30f, w3 = 0.30f;
        const float w4 = 0.60f, w5 = 0.40f;
        m.VPD = w1 * m.VMD_norm + w2 * m.TTE_norm + w3 * m.TSD_norm;
        m.PRD = w4 * m.APT_norm + w5 * m.CPR_norm;
        return m;
    }

    /// <summary>
    /// 全量评估：在静态评估（M1~M5）基础上追加蒙特卡洛（M6~M8）与综合聚合。
    /// mcRuns 可降采样（搜索期）或全量（最终复核），默认 FULL_MC 次。
    /// </summary>
    private static DifficultyAnalyzer.DifficultyMetrics EvaluateOnGrid(
        int?[,] grid, int rows, int cols, int typeCountHint, int mcRuns = FULL_MC)
    {
        var m = EvaluateStaticOnGrid(grid, rows, cols, typeCountHint);
        if (mcRuns <= 0) return m;

        Dictionary<int, List<PathFinder.Point>> typeGroups = CollectTypeGroups(grid, rows, cols);

        // M6/M7/M8 模拟
        float avgDW, df, logSB;
        SimulateGame(grid, rows, cols, typeGroups, out avgDW, out df, out logSB, mcRuns);
        m.DW = avgDW; m.DF = df; m.logSB = logSB;

        // 归一化 M6~M8
        const float DW_LOW = 2f, DW_MID = 8f, SB_LOW = 5f, SB_MID = 40f;
        m.DW_norm = NormU(m.DW, DW_LOW, DW_MID, Mathf.Max(1, m.totalPairs));
        m.DF_norm = Mathf.Clamp01(m.DF);
        m.SB_norm = NormU(m.logSB, SB_LOW, SB_MID, 100f);

        // 聚合 SPD + DD
        const float w6 = 0.40f, w7 = 0.30f, w8 = 0.30f;
        const float alpha = 0.30f, beta = 0.45f, gamma = 0.25f;
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
    private static void SimulateGame(int?[,] grid0, int rows, int cols,
        Dictionary<int, List<PathFinder.Point>> typeGroups0,
        out float avgDW, out float df, out float logSB, int mcRuns = FULL_MC)
    {
        avgDW = 0f; df = 0f; logSB = 0f;
        float tDW = 0f, tSB = 0f;
        int tDead = 0, tSteps = 0;
        System.Random rng = new System.Random();
        for (int run = 0; run < mcRuns; run++)
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
        avgDW = tDW / mcRuns;
        df = tSteps > 0 ? (float)tDead / tSteps : 0f;
        logSB = tSB / mcRuns;
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
