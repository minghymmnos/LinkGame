using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 关卡和通关记录持久化管理器。
/// - 保存所有已生成的关卡列表（LevelInstance[]）到 PlayerPrefs
/// - 保存每个关卡的通关时间记录（可追加、可单条删除、可全部清空）
/// 采用 JsonUtility 序列化。单例模式。
/// </summary>
public class LevelRecordManager : MonoBehaviour
{
    private const string PP_KEY_LEVELS = "LLK_GeneratedLevels_v1";

    private static LevelRecordManager instance;
    /// <summary>单例访问器</summary>
    public static LevelRecordManager Instance => instance;

    // 内存缓存：已生成的关卡列表
    private List<LevelInstance> levelList = new List<LevelInstance>();

    private void Awake()
    {
        instance = this;
        LoadAll();
    }

    /// <summary>从 PlayerPrefs 读取并反序列化所有关卡和记录。</summary>
    public void LoadAll()
    {
        levelList = new List<LevelInstance>();
        try
        {
            string json = PlayerPrefs.GetString(PP_KEY_LEVELS, "");
            if (string.IsNullOrEmpty(json)) return;
            // JsonUtility 不支持直接反序列化 List<T>，故包一层容器类
            WrappedLevels w = JsonUtility.FromJson<WrappedLevels>(json);
            if (w != null && w.levels != null)
                levelList = w.levels;
        }
        catch (Exception e)
        {
            Debug.LogError("读取关卡记录失败：" + e.Message);
            levelList = new List<LevelInstance>();
        }
    }

    /// <summary>将内存中的所有关卡+记录序列化写入 PlayerPrefs。</summary>
    public void SaveAll()
    {
        try
        {
            WrappedLevels w = new WrappedLevels { levels = levelList };
            string json = JsonUtility.ToJson(w);
            PlayerPrefs.SetString(PP_KEY_LEVELS, json);
            PlayerPrefs.Save();
        }
        catch (Exception e)
        {
            Debug.LogError("保存关卡记录失败：" + e.Message);
        }
    }

    /// <summary>获取当前所有已生成关卡（按创建时间倒序，最新在前）。</summary>
    public List<LevelInstance> GetAllLevels()
    {
        // 返回拷贝，防止外部篡改
        return new List<LevelInstance>(levelList);
    }

    /// <summary>GetAllLevels 的别名（与 UIManager 旧调用兼容）。</summary>
    public List<LevelInstance> GetLevels() => GetAllLevels();

    /// <summary>按 levelId 查找关卡。</summary>
    public LevelInstance FindLevel(string levelId)
    {
        if (string.IsNullOrEmpty(levelId)) return null;
        return levelList.Find(l => l.levelId == levelId);
    }

    /// <summary>
    /// 将新生成的关卡加入列表并持久化。
    /// 如果 levelId 已存在则覆盖（用于刷新指标）。
    /// </summary>
    public void UpsertLevel(LevelInstance level)
    {
        if (level == null) return;
        int idx = levelList.FindIndex(l => l.levelId == level.levelId);
        if (idx >= 0) levelList[idx] = level;
        else levelList.Insert(0, level);
        SaveAll();
    }

    /// <summary>从列表中移除某个关卡（含其所有通关记录），并持久化。</summary>
    public void DeleteLevel(string levelId)
    {
        int idx = levelList.FindIndex(l => l.levelId == levelId);
        if (idx >= 0)
        {
            levelList.RemoveAt(idx);
            SaveAll();
        }
    }

    /// <summary>
    /// 追加一条通关时间记录。同时更新 bestTime。
    /// </summary>
    /// <param name="levelId">关卡 ID</param>
    /// <param name="clearTime">通关用时（秒）</param>
    /// <returns>true = 成功追加，false = 未找到关卡</returns>
    public bool AddClearRecord(string levelId, float clearTime)
    {
        LevelInstance lv = FindLevel(levelId);
        if (lv == null) return false;

        if (lv.clearRecords == null) lv.clearRecords = new List<float>();
        if (lv.recordTimestamps == null) lv.recordTimestamps = new List<string>();

        lv.clearRecords.Add(clearTime);
        lv.recordTimestamps.Add(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        if (lv.bestTime < 0 || clearTime < lv.bestTime)
            lv.bestTime = clearTime;

        SaveAll();
        return true;
    }

    /// <summary>
    /// 删除某关卡的一条通关记录（按索引），并重新计算 bestTime。
    /// </summary>
    public void DeleteClearRecord(string levelId, int recordIndex)
    {
        LevelInstance lv = FindLevel(levelId);
        if (lv == null) return;
        if (recordIndex < 0 || recordIndex >= lv.clearRecords.Count) return;

        lv.clearRecords.RemoveAt(recordIndex);
        if (recordIndex < lv.recordTimestamps.Count)
            lv.recordTimestamps.RemoveAt(recordIndex);

        // 重新计算最佳时间
        if (lv.clearRecords.Count == 0)
            lv.bestTime = -1f;
        else
        {
            float best = float.MaxValue;
            for (int i = 0; i < lv.clearRecords.Count; i++)
                if (lv.clearRecords[i] < best) best = lv.clearRecords[i];
            lv.bestTime = best;
        }
        SaveAll();
    }

    /// <summary>清空某关卡的所有通关记录。</summary>
    public void ClearAllRecordsOf(string levelId)
    {
        LevelInstance lv = FindLevel(levelId);
        if (lv == null) return;
        lv.clearRecords.Clear();
        lv.recordTimestamps.Clear();
        lv.bestTime = -1f;
        SaveAll();
    }

    /// <summary>清空所有已生成的关卡和记录（危险操作，慎用）。</summary>
    public void ClearAll()
    {
        levelList.Clear();
        SaveAll();
    }

    // ---------- JSON 包装类 ----------
    [Serializable]
    private class WrappedLevels
    {
        public List<LevelInstance> levels;
    }
}
