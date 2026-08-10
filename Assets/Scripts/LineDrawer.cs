using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 连线绘制器。
/// 在两个匹配的瓦片之间绘制金色连线，并在短暂显示后自动清除。
/// 使用 LineRenderer 组件绘制连线，通过协程实现延迟清除。
/// </summary>
public class LineDrawer : MonoBehaviour
{
    private GameObject linePrefab;  // 连线预制体（含 LineRenderer 配置）
    private float lineWidth = 0.08f; // 连线宽度
    private float showDuration = 0.3f; // 连线显示时长（秒）

    // 当前激活的连线列表，用于统一管理
    private List<LineRenderer> activeLines = new List<LineRenderer>();

    /// <summary>
    /// 设置连线预制体。由 GameInitializer 在初始化时调用。
    /// </summary>
    /// <param name="prefab">连线预制体</param>
    public void SetLinePrefab(GameObject prefab)
    {
        linePrefab = prefab;
    }

    /// <summary>
    /// 绘制路径连线。
    /// 根据路径点列表创建 LineRenderer，设置世界坐标，并在延迟后清除。
    /// </summary>
    /// <param name="path">路径点列表（网格坐标）</param>
    /// <param name="onComplete">连线清除后的回调（GameController 在此执行消除逻辑）</param>
    public void DrawPath(List<PathFinder.Point> path, System.Action onComplete)
    {
        // 若预制体未设置，创建一个默认的
        if (linePrefab == null)
        {
            GameObject obj = new GameObject("LineInstance");
            linePrefab = obj;
            LineRenderer lr = obj.AddComponent<LineRenderer>();
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startColor = new Color(1f, 0.8f, 0.2f); // 金色
            lr.endColor = new Color(1f, 0.8f, 0.2f);
            lr.startWidth = lineWidth;
            lr.endWidth = lineWidth;
            lr.positionCount = 0;
            lr.useWorldSpace = true;
        }

        // 实例化连线对象
        GameObject lineObj = Instantiate(linePrefab, transform);
        lineObj.SetActive(true);

        LineRenderer line = lineObj.GetComponent<LineRenderer>();
        if (line == null)
            line = lineObj.AddComponent<LineRenderer>();

        // 设置连线的点数和宽度
        line.positionCount = path.Count;
        line.startWidth = lineWidth;
        line.endWidth = lineWidth;

        // 将每个路径点的网格坐标转换为世界坐标
        Vector3[] positions = new Vector3[path.Count];
        for (int i = 0; i < path.Count; i++)
        {
            positions[i] = GridManager.GridToWorldPosition(path[i].Row, path[i].Col);
            positions[i].z = -1; // 设置 Z 坐标为 -1，使连线显示在瓦片前方
        }
        line.SetPositions(positions);

        activeLines.Add(line); // 加入激活列表

        // 启动延迟清除协程
        StartCoroutine(ClearLineAfterDelay(line, onComplete));
    }

    /// <summary>
    /// 延迟清除连线的协程。
    /// 等待 showDuration 后销毁 LineRenderer，并触发消除回调。
    /// </summary>
    /// <param name="line">要清除的连线</param>
    /// <param name="onComplete">清除后的回调</param>
    private System.Collections.IEnumerator ClearLineAfterDelay(LineRenderer line, System.Action onComplete)
    {
        yield return new WaitForSeconds(showDuration); // 等待显示时长

        // 从激活列表中移除并销毁
        if (activeLines.Contains(line))
        {
            activeLines.Remove(line);
            Destroy(line.gameObject);
        }

        // 触发回调，通知 GameController 执行消除
        if (onComplete != null)
            onComplete();
    }

    /// <summary>
    /// 清除所有连线和协程。
    /// 由 GameController.StartNewGame 在重置游戏时调用。
    /// </summary>
    public void ClearAllLines()
    {
        StopAllCoroutines(); // 停止所有延迟清除协程
        foreach (var line in activeLines)
        {
            if (line != null)
                Destroy(line.gameObject);
        }
        activeLines.Clear();
    }
}
