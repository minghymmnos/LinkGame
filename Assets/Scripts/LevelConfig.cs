using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 难度等级枚举。与 GetDifficultyLevel() 的五个等级一一对应。
/// 用于 UI 下拉框选择和指标↔等级双向转换。
/// </summary>
public enum DifficultyGrade
{
    VeryEasy = 0,   // 极易：DD ∈ [0, 0.2)
    Easy = 1,       // 简单：DD ∈ [0.2, 0.4)
    Normal = 2,     // 普通：DD ∈ [0.4, 0.6)
    Hard = 3,       // 困难：DD ∈ [0.6, 0.8)
    VeryHard = 4,   // 极难：DD ∈ [0.8, 1.0]
}

/// <summary>
/// 难度等级辅助工具。
/// </summary>
public static class DifficultyGradeUtil
{
    /// <summary>难度等级下限（含）</summary>
    public static readonly float[] GradeLower = { 0.00f, 0.20f, 0.40f, 0.60f, 0.80f };
    /// <summary>难度等级上限（不含，最后一个等级含）</summary>
    public static readonly float[] GradeUpper = { 0.20f, 0.40f, 0.60f, 0.80f, 1.01f };
    /// <summary>中文名称（按枚举顺序）</summary>
    public static readonly string[] GradeNames = { "极易", "简单", "普通", "困难", "极难" };

    /// <summary>根据 DD 数值返回难度等级。</summary>
    public static DifficultyGrade GetGrade(float dd)
    {
        dd = Mathf.Clamp01(dd);
        for (int i = 4; i >= 0; i--)
            if (dd >= GradeLower[i]) return (DifficultyGrade)i;
        return DifficultyGrade.VeryEasy;
    }

    /// <summary>返回指定等级的中文名称。</summary>
    public static string GetName(DifficultyGrade g) => GradeNames[(int)g];

    /// <summary>GetName 的别名（UIManager 调用兼容）。</summary>
    public static string ToName(DifficultyGrade g) => GetName(g);

    /// <summary>返回等级对应的 UI 颜色（绿色易→红色难）。</summary>
    public static Color ToColor(DifficultyGrade g)
    {
        switch (g)
        {
            case DifficultyGrade.VeryEasy: return new Color(0.35f, 0.85f, 0.45f);
            case DifficultyGrade.Easy:     return new Color(0.45f, 0.80f, 0.60f);
            case DifficultyGrade.Normal:   return new Color(0.95f, 0.82f, 0.30f);
            case DifficultyGrade.Hard:     return new Color(0.95f, 0.55f, 0.20f);
            case DifficultyGrade.VeryHard: return new Color(0.90f, 0.30f, 0.30f);
            default:                       return Color.white;
        }
    }

    /// <summary>在指定等级的数值区间内随机生成一个归一化指标值。</summary>
    public static float RandomInGrade(DifficultyGrade g, System.Random rng = null)
    {
        int i = (int)g;
        float lo = GradeLower[i];
        float hi = (i == 4) ? 1.00f : GradeUpper[i];
        float r = (float)((rng ?? new System.Random()).NextDouble());
        return lo + r * (hi - lo);
    }
}

/// <summary>
/// 8 个难度指标名称枚举，用于 UI 控件定位和约束字典。
/// </summary>
public enum MetricId
{
    VMD = 0, TTE = 1, TSD = 2,
    APT = 3, CPR = 4,
    DW = 5, DF = 6, SB = 7,
}

/// <summary>
/// 单个难度指标的约束条件（玩家在关卡设计界面设置）。
/// 两种模式互斥：指定归一化值 —— 或指定难度等级（区间随机）。
/// </summary>
[Serializable]
public class MetricConstraint
{
    public MetricId id;                 // 指标 ID
    public bool useGrade;               // true：按等级随机；false：按指定归一值
    public float normalizedValue;       // useGrade=false 时使用：[0,1]
    public DifficultyGrade grade;       // useGrade=true 时使用：指定等级区间
}

/// <summary>
/// 关卡配置。包含生成一个关卡所需的全部参数：网格尺寸、图标类型数、8 个指标约束。
/// 可在关卡设计器中由玩家手动设置，也可由 LevelGenerator 反序列化复用。
/// </summary>
[Serializable]
public class LevelConfig
{
    public int rows;                    // 网格行数（默认 8）
    public int cols;                    // 网格列数（默认 10）
    public int typeCount;               // 图标类型数 K（默认 8）
    public List<MetricConstraint> metrics; // 8 个指标约束（长度必须为 8）
    public int generateCount;           // 玩家要求生成的关卡数量 N（仅设计界面使用）

    public LevelConfig()
    {
        rows = 8; cols = 10; typeCount = 8; generateCount = 5;
        metrics = new List<MetricConstraint>(8);
        for (int i = 0; i < 8; i++)
            metrics.Add(new MetricConstraint
            {
                id = (MetricId)i,
                useGrade = false,
                normalizedValue = 0.5f,
                grade = DifficultyGrade.Normal,
            });
    }
}

/// <summary>
/// 已生成的单个关卡实例。
/// 保存：关卡 ID、创建时间、使用的配置、难度评价结果（DD+等级）、玩家通关用时记录列表。
/// gridDataSnapshot 为 R×C 的 int? 压缩表示：-1 代表 null，≥0 代表类型 ID。
/// </summary>
[Serializable]
public class LevelInstance
{
    public string levelId;                 // 唯一 ID（GUID 10 位）
    /// <summary>levelId 的别名，兼容 UIManager 用 .id 访问。</summary>
    public string id { get { return levelId; } set { levelId = value; } }

    public string createdAt;               // 创建时间（DateTime.ToString）
    public LevelConfig config;             // 生成时使用的配置（含 8 指标约束）
    public DifficultyAnalyzer.DifficultyMetrics metrics; // 该关卡实际评价结果

    /// <summary>综合难度 DD（metrics.DD 的快捷访问 + 兼容序列化独立存储）。</summary>
    public float overallDD
    {
        get { return metrics != null ? metrics.DD : _overallDD; }
        set { _overallDD = value; if (metrics != null) metrics.DD = value; }
    }
    [SerializeField] private float _overallDD;

    /// <summary>该关卡生成时是否触发单关卡超时（true 表示用 bestCost 兜底，指标可能略偏离目标）。</summary>
    public bool generationTimedOut;

    /// <summary>
    /// 生成备注：空字符串表示正常生成；非空时说明本次关卡的生成状态。
    /// 当超时兜底为随机生成时写入「超时生成失败，已按给定行列与配对类型数随机生成一个关卡」，
    /// 供卡片 UI 展示给玩家确认。
    /// </summary>
    public string remark;

    public List<int> gridSnapshot;         // 压缩快照：长度=(rows+2)*(cols+2)，-1=null，≥0=类型ID
    public float bestTime;                 // 最佳通关时间（秒），-1 表示未通关
    public List<float> clearRecords;       // 所有通关时间记录（秒）
    public List<string> recordTimestamps;  // 每次通关的记录时间（与 clearRecords 对齐）

    public LevelInstance()
    {
        // —— 关键：所有引用类型字段在构造函数里 new，保证经 PlayerPrefs JSON 反序列化后永不为 null ——
        // 之前 config/metrics/gridSnapshot 未初始化：当 JSON 里这些字段被省略（默认值压缩）时会保持 null，
        // 导致 GameController.StartNewGame 中条件 (level.config!=null) 不满足，currentLevelId 漏赋值，
        // 通关时 OnLevelCleared 传 null → OnGameLevelCleared(string.IsNullOrEmpty) 直接 return 跳过记录保存。
        config = new LevelConfig();
        metrics = new DifficultyAnalyzer.DifficultyMetrics();
        gridSnapshot = new List<int>();
        clearRecords = new List<float>();
        recordTimestamps = new List<string>();
        bestTime = -1f;
        _overallDD = 0f;
        generationTimedOut = false;
        remark = string.Empty;
        levelId = string.Empty;
        createdAt = string.Empty;
    }
}
