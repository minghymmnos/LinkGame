using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 难度分析器。
/// 计算连连看关卡的 8 个量化难度指标和综合难度评分。
/// 所有指标基于"连连看关卡生成设计方案"文档定义。
/// 这是一个静态工具类，无需挂载到 GameObject 上。
/// </summary>
public static class DifficultyAnalyzer
{
    /// <summary>
    /// 难度指标计算结果。
    /// 包含 8 个原始指标值、归一化值和综合难度评分。
    /// </summary>
    public class DifficultyMetrics
    {
        // ---------- 第一组：视觉感知难度（VPD）----------

        // M1 有效解密度 VMD：初始可连通配对数 / 理论配对总数，越低越难
        public float VMD;
        public float VMD_norm; // 归一化值 [0,1]，1=最难

        // M2 图标类型熵 TTE：类型分布的香农熵，越高越难
        public float TTE;
        public float TTE_norm;

        // M3 同类型空间离散度 TSD：同类型瓦片平均曼哈顿距离，越高越难
        public float TSD;
        public float TSD_norm;

        // ---------- 第二组：路径推理难度（PRD）----------

        // M4 平均路径转弯数 APT：可连通配对路径转弯数平均值，越高越难
        public float APT;
        public float APT_norm;

        // M5 复杂路径占比 CPR：需 2 拐角路径的配对比例，越高越难
        public float CPR;
        public float CPR_norm;

        // ---------- 第三组：策略规划难度（SPD）----------

        // M6 决策宽度 DW：游戏中每步可选有效配对数平均值，U 型曲线
        public float DW;
        public float DW_norm;

        // M7 死锁频率 DF：模拟中无有效移动的发生频率，越高越难
        public float DF;
        public float DF_norm;

        // M8 解序列分支度 SB（对数形式）：消除顺序自由度，U 型曲线
        public float logSB;
        public float SB_norm;

        // ---------- 维度得分和综合难度 ----------

        public float VPD; // 视觉感知难度 [0,1]
        public float PRD; // 路径推理难度 [0,1]
        public float SPD; // 策略规划难度 [0,1]
        public float DD;  // 综合难度评分 [0,1]
        public string difficultyLevel; // 难度等级文字描述

        // ---------- 中间计算数据 ----------

        public int totalPairs;        // 理论配对总数 TPP
        public int validPairCount;    // 当前可连通配对数 VPC
        public int numTypes;          // 类型数 K
        public int totalTiles;        // 总瓦片数 N
    }

    // ---------- 归一化权重（可调）----------

    // 维度内指标权重
    private const float w1 = 0.40f; // VMD
    private const float w2 = 0.30f; // TTE
    private const float w3 = 0.30f; // TSD

    private const float w4 = 0.60f; // APT
    private const float w5 = 0.40f; // CPR

    private const float w6 = 0.40f; // DW
    private const float w7 = 0.30f; // DF
    private const float w8 = 0.30f; // SB

    // 维度权重（α+β+γ=1）
    private const float alpha = 0.30f; // VPD 视觉感知
    private const float beta  = 0.45f; // PRD 路径推理（核心，权重最高）
    private const float gamma  = 0.25f; // SPD 策略规划

    // M6/M8 的 U 型归一化分界点
    private const float DW_LOW = 2f;   // 低于此值选择太少
    private const float DW_MID = 8f;   // 高于此值选择太多
    private const float SB_LOW = 5f;   // 对数形式，低于此值顺序受限
    private const float SB_MID = 40f;   // 对数形式，高于此值搜索空间大

    // 蒙特卡洛模拟参数
    private const int MONTE_CARLO_RUNS = 100; // 死锁频率模拟次数

    /// <summary>
    /// 计算关卡的完整难度指标。
    /// 在关卡生成后（StartNewGame 之后）调用，分析初始网格状态。
    /// </summary>
    /// <returns>包含所有指标的结果对象</returns>
    public static DifficultyMetrics Analyze()
    {
        DifficultyMetrics m = new DifficultyMetrics();

        GridManager grid = GridManager.Instance;
        if (grid == null || grid.GridData == null)
            return m;

        int rows = grid.Rows;
        int cols = grid.Cols;
        m.totalTiles = rows * cols;

        // 收集所有瓦片，按类型分组
        Dictionary<int, List<PathFinder.Point>> typeGroups = new Dictionary<int, List<PathFinder.Point>>();
        for (int r = 1; r <= rows; r++)
        {
            for (int c = 1; c <= cols; c++)
            {
                if (grid.GridData[r, c] != null)
                {
                    int typeId = grid.GridData[r, c].Value;
                    if (!typeGroups.ContainsKey(typeId))
                        typeGroups[typeId] = new List<PathFinder.Point>();
                    typeGroups[typeId].Add(new PathFinder.Point(r, c));
                }
            }
        }

        m.numTypes = typeGroups.Count;

        // 计算理论配对总数 TPP = Σ C(n_i, 2)
        m.totalPairs = 0;
        foreach (var kvp in typeGroups)
            m.totalPairs += kvp.Value.Count * (kvp.Value.Count - 1) / 2;

        // 计算可连通配对及路径转弯数
        int validCount = 0;
        int twoTurnCount = 0;
        int totalTurns = 0;

        foreach (var kvp in typeGroups)
        {
            List<PathFinder.Point> tiles = kvp.Value;
            for (int i = 0; i < tiles.Count; i++)
            {
                for (int j = i + 1; j < tiles.Count; j++)
                {
                    List<PathFinder.Point> path = PathFinder.FindPath(
                        grid.GridData, rows, cols, tiles[i], tiles[j]);

                    if (path != null)
                    {
                        validCount++;
                        int turns = path.Count - 2; // 转弯数 = 路径点数 - 2
                        totalTurns += turns;
                        if (turns == 2)
                            twoTurnCount++;
                    }
                }
            }
        }

        m.validPairCount = validCount;

        // ---------- 计算 M1-M8 原始值 ----------

        // M1 VMD：有效解密度
        m.VMD = m.totalPairs > 0 ? (float)validCount / m.totalPairs : 0f;

        // M2 TTE：类型熵
        m.TTE = CalculateTypeEntropy(typeGroups, m.totalTiles);

        // M3 TSD：空间离散度
        m.TSD = CalculateSpatialDispersion(typeGroups);

        // M4 APT：平均转弯数
        m.APT = validCount > 0 ? (float)totalTurns / validCount : 0f;

        // M5 CPR：复杂路径占比
        m.CPR = validCount > 0 ? (float)twoTurnCount / validCount : 0f;

        // M6/M7/M8：需要模拟完整游戏过程
        SimulateGameplay(grid, typeGroups, out float avgDW, out float deadlockFreq, out float logSB);
        m.DW = avgDW;
        m.DF = deadlockFreq;
        m.logSB = logSB;

        // ---------- 归一化 ----------

        // M1 VMD：越低越难 → 取反
        m.VMD_norm = 1f - m.VMD;

        // M2 TTE：除以最大熵 log2(K)
        float maxEntropy = Mathf.Log(m.numTypes, 2);
        m.TTE_norm = maxEntropy > 0 ? m.TTE / maxEntropy : 0f;

        // M3 TSD：线性归一化 [1, R+C-2] → [0,1]
        float maxDist = rows + cols - 2;
        m.TSD_norm = maxDist > 1 ? (m.TSD - 1f) / (maxDist - 1f) : 0f;

        // M4 APT：除以 2
        m.APT_norm = m.APT / 2f;

        // M5 CPR：已归一化
        m.CPR_norm = m.CPR;

        // M6 DW：U 型归一化
        m.DW_norm = NormalizeUShape(m.DW, DW_LOW, DW_MID, m.totalPairs);

        // M7 DF：已归一化
        m.DF_norm = m.DF;

        // M8 SB：U 型归一化
        m.SB_norm = NormalizeUShape(m.logSB, SB_LOW, SB_MID, 100f);

        // ---------- 维度聚合 ----------

        m.VPD = w1 * m.VMD_norm + w2 * m.TTE_norm + w3 * m.TSD_norm;
        m.PRD = w4 * m.APT_norm + w5 * m.CPR_norm;
        m.SPD = w6 * m.DW_norm + w7 * m.DF_norm + w8 * m.SB_norm;

        // 综合难度
        m.DD = alpha * m.VPD + beta * m.PRD + gamma * m.SPD;
        m.DD = Mathf.Clamp01(m.DD);

        // 难度等级
        m.difficultyLevel = GetDifficultyLevel(m.DD);

        return m;
    }

    /// <summary>
    /// 计算类型熵 TTE。
    /// 香农熵 = -Σ p_i × log2(p_i)
    /// </summary>
    private static float CalculateTypeEntropy(Dictionary<int, List<PathFinder.Point>> typeGroups, int totalTiles)
    {
        if (totalTiles == 0) return 0f;

        float entropy = 0f;
        foreach (var kvp in typeGroups)
        {
            float p = (float)kvp.Value.Count / totalTiles;
            if (p > 0)
                entropy -= p * Mathf.Log(p, 2);
        }
        return entropy;
    }

    /// <summary>
    /// 计算空间离散度 TSD。
    /// 对每种类型计算其瓦片两两之间的平均曼哈顿距离，再对所有类型取平均。
    /// </summary>
    private static float CalculateSpatialDispersion(Dictionary<int, List<PathFinder.Point>> typeGroups)
    {
        if (typeGroups.Count == 0) return 0f;

        float totalDispersion = 0f;
        int typeCount = 0;

        foreach (var kvp in typeGroups)
        {
            List<PathFinder.Point> tiles = kvp.Value;
            if (tiles.Count < 2) continue;

            long distSum = 0;
            int pairCount = 0;

            for (int i = 0; i < tiles.Count; i++)
            {
                for (int j = i + 1; j < tiles.Count; j++)
                {
                    distSum += Mathf.Abs(tiles[i].Row - tiles[j].Row) +
                              Mathf.Abs(tiles[i].Col - tiles[j].Col);
                    pairCount++;
                }
            }

            if (pairCount > 0)
            {
                totalDispersion += (float)distSum / pairCount;
                typeCount++;
            }
        }

        return typeCount > 0 ? totalDispersion / typeCount : 0f;
    }

    /// <summary>
    /// 模拟完整游戏过程，计算 M6（决策宽度）、M7（死锁频率）、M8（解序列分支度）。
    /// 使用随机配对选择策略（非最优），更接近真实玩家行为。
    /// 多次模拟取平均以提高稳定性。
    /// </summary>
    private static void SimulateGameplay(GridManager grid, Dictionary<int, List<PathFinder.Point>> typeGroups,
        out float avgDW, out float deadlockFreq, out float logSB)
    {
        avgDW = 0f;
        deadlockFreq = 0f;
        logSB = 0f;

        if (MONTE_CARLO_RUNS <= 0) return;

        int rows = grid.Rows;
        int cols = grid.Cols;

        float totalDW = 0f;
        int totalDeadlocks = 0;
        int totalSteps = 0;
        float totalLogSB = 0f;

        for (int run = 0; run < MONTE_CARLO_RUNS; run++)
        {
            // 复制网格数据用于模拟
            int?[,] simGrid = CopyGrid(grid.GridData);

            int stepDW = 0;
            int stepCount = 0;
            int deadlockCount = 0;
            float stepLogSB = 0f;

            // 模拟直到网格清空
            while (true)
            {
                // 收集当前可连通配对
                List<(PathFinder.Point p1, PathFinder.Point p2, int turns)> validPairs =
                    FindAllValidPairs(simGrid, rows, cols);

                int vpc = validPairs.Count;

                if (vpc == 0)
                {
                    // 无可连通配对
                    bool hasRemaining = HasRemainingTilesInSim(simGrid, rows, cols);
                    if (!hasRemaining)
                        break; // 通关

                    // 死锁：有剩余但无可消对，模拟重排
                    deadlockCount++;
                    ShuffleSimGrid(simGrid, rows, cols);
                    continue;
                }

                // 累计 M6 决策宽度
                stepDW += vpc;
                stepCount++;

                // 累计 M8 解序列分支度（对数）
                stepLogSB += Mathf.Log(vpc, 10); // 用 log10 避免溢出

                // 随机选择一个配对消除
                int pick = Random.Range(0, vpc);
                var pair = validPairs[pick];
                simGrid[pair.p1.Row, pair.p1.Col] = null;
                simGrid[pair.p2.Row, pair.p2.Col] = null;
            }

            totalDW += stepCount > 0 ? (float)stepDW / stepCount : 0f;
            totalDeadlocks += deadlockCount;
            totalSteps += stepCount;
            totalLogSB += stepLogSB;
        }

        avgDW = totalDW / MONTE_CARLO_RUNS;
        deadlockFreq = totalSteps > 0 ? (float)totalDeadlocks / totalSteps : 0f;
        logSB = totalLogSB / MONTE_CARLO_RUNS;
    }

    /// <summary>
    /// 查找当前网格中所有可连通配对。
    /// </summary>
    private static List<(PathFinder.Point p1, PathFinder.Point p2, int turns)> FindAllValidPairs(
        int?[,] grid, int rows, int cols)
    {
        List<(PathFinder.Point, PathFinder.Point, int)> pairs = new List<(PathFinder.Point, PathFinder.Point, int)>();

        // 收集所有瓦片按类型分组
        Dictionary<int, List<PathFinder.Point>> groups = new Dictionary<int, List<PathFinder.Point>>();
        for (int r = 1; r <= rows; r++)
        {
            for (int c = 1; c <= cols; c++)
            {
                if (grid[r, c] != null)
                {
                    int typeId = grid[r, c].Value;
                    if (!groups.ContainsKey(typeId))
                        groups[typeId] = new List<PathFinder.Point>();
                    groups[typeId].Add(new PathFinder.Point(r, c));
                }
            }
        }

        foreach (var kvp in groups)
        {
            List<PathFinder.Point> tiles = kvp.Value;
            for (int i = 0; i < tiles.Count; i++)
            {
                for (int j = i + 1; j < tiles.Count; j++)
                {
                    List<PathFinder.Point> path = PathFinder.FindPath(grid, rows, cols, tiles[i], tiles[j]);
                    if (path != null)
                    {
                        int turns = path.Count - 2;
                        pairs.Add((tiles[i], tiles[j], turns));
                    }
                }
            }
        }

        return pairs;
    }

    /// <summary>
    /// 检查模拟网格中是否还有剩余瓦片。
    /// </summary>
    private static bool HasRemainingTilesInSim(int?[,] grid, int rows, int cols)
    {
        for (int r = 1; r <= rows; r++)
            for (int c = 1; c <= cols; c++)
                if (grid[r, c] != null)
                    return true;
        return false;
    }

    /// <summary>
    /// 模拟重排：打乱剩余瓦片的类型分配。
    /// </summary>
    private static void ShuffleSimGrid(int?[,] grid, int rows, int cols)
    {
        List<int> remainingTypes = new List<int>();
        List<(int r, int c)> positions = new List<(int, int)>();

        for (int r = 1; r <= rows; r++)
        {
            for (int c = 1; c <= cols; c++)
            {
                if (grid[r, c] != null)
                {
                    remainingTypes.Add(grid[r, c].Value);
                    positions.Add((r, c));
                }
            }
        }

        // Fisher-Yates 洗牌
        for (int i = remainingTypes.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            int temp = remainingTypes[i];
            remainingTypes[i] = remainingTypes[j];
            remainingTypes[j] = temp;
        }

        for (int i = 0; i < positions.Count; i++)
            grid[positions[i].r, positions[i].c] = remainingTypes[i];
    }

    /// <summary>
    /// 复制网格数据，用于模拟（不修改原始数据）。
    /// </summary>
    private static int?[,] CopyGrid(int?[,] source)
    {
        int rows = source.GetLength(0);
        int cols = source.GetLength(1);
        int?[,] copy = new int?[rows, cols];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                copy[r, c] = source[r, c];
        return copy;
    }

    /// <summary>
    /// U 型归一化。
    /// 低于 low 时难度为 1（选择太少），在 [low, mid] 区间线性下降到 0，
    /// 超过 mid 后线性上升到 1（选择太多）。
    /// </summary>
    /// <param name="value">原始值</param>
    /// <param name="low">难度=1 的下界</param>
    /// <param name="mid">难度=0 的中点</param>
    /// <param name="high">难度=1 的上界</param>
    private static float NormalizeUShape(float value, float low, float mid, float high)
    {
        if (value <= low)
            return 1f;
        if (value <= mid)
            return 1f - (value - low) / (mid - low);
        // value > mid
        if (high <= mid) return 0f;
        return Mathf.Clamp01((value - mid) / (high - mid));
    }

    /// <summary>
    /// 根据综合难度评分返回难度等级描述。
    /// </summary>
    private static string GetDifficultyLevel(float dd)
    {
        if (dd < 0.20f) return "极易";
        if (dd < 0.40f) return "简单";
        if (dd < 0.60f) return "普通";
        if (dd < 0.80f) return "困难";
        return "极难";
    }

    /// <summary>
    /// 将指标格式化为多行文本，用于 UI 显示。
    /// </summary>
    public static string FormatMetrics(DifficultyMetrics m)
    {
        if (m == null) return "无法计算难度指标";

        return $"综合难度: {m.DD:F3} ({m.difficultyLevel})\n" +
               $"─────────────────\n" +
               $"视觉感知 VPD: {m.VPD:F3}\n" +
               $"  M1 有效解密度: {m.VMD:F3} (归一: {m.VMD_norm:F3})\n" +
               $"  M2 类型熵: {m.TTE:F3} (归一: {m.TTE_norm:F3})\n" +
               $"  M3 空间离散度: {m.TSD:F2} (归一: {m.TSD_norm:F3})\n" +
               $"─────────────────\n" +
               $"路径推理 PRD: {m.PRD:F3}\n" +
               $"  M4 平均转弯数: {m.APT:F3} (归一: {m.APT_norm:F3})\n" +
               $"  M5 复杂路径占比: {m.CPR:F3} (归一: {m.CPR_norm:F3})\n" +
               $"─────────────────\n" +
               $"策略规划 SPD: {m.SPD:F3}\n" +
               $"  M6 决策宽度: {m.DW:F2} (归一: {m.DW_norm:F3})\n" +
               $"  M7 死锁频率: {m.DF:F3} (归一: {m.DF_norm:F3})\n" +
               $"  M8 解序列分支度: log={m.logSB:F2} (归一: {m.SB_norm:F3})\n" +
               $"─────────────────\n" +
               $"类型数: {m.numTypes}  总瓦片: {m.totalTiles}\n" +
               $"可连通对: {m.validPairCount}/{m.totalPairs}";
    }
}
