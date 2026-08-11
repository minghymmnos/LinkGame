using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bot 控制器。
/// 实现自动游戏功能，自动寻找最优配对并执行消除。
/// 通过协程驱动主循环，按节奏依次点击瓦片，让玩家能观察 Bot 的操作过程。
/// 采用单例模式。
/// </summary>
public class BotController : MonoBehaviour
{
    private static BotController instance;
    private bool isRunning = false;             // Bot 是否运行中
    private Coroutine botCoroutine = null;       // 主循环协程引用

    /// <summary>单例访问器</summary>
    public static BotController Instance => instance;
    /// <summary>Bot 是否运行中（只读），供 UIManager 查询按钮文字</summary>
    public bool IsRunning => isRunning;

    /// <summary>等价于 StopBot()，兼容 UIManager/GameController 调用。</summary>
    public void Stop() => StopBot();

    private void Awake()
    {
        instance = this; // 设置单例
    }

    /// <summary>
    /// 切换 Bot 启停状态。由 UIManager 的 Bot 按钮调用。
    /// </summary>
    public void ToggleBot()
    {
        if (isRunning)
            StopBot();
        else
            StartBot();
    }

    /// <summary>
    /// 启动 Bot。
    /// 标记运行状态，启动主循环协程。
    /// </summary>
    public void StartBot()
    {
        if (isRunning) return; // 防止重复启动
        isRunning = true;
        botCoroutine = StartCoroutine(BotLoop());
        Debug.Log("Bot 已启动");
    }

    /// <summary>
    /// 停止 Bot。
    /// 清除运行状态，停止协程，清除残留选中，更新 UI。
    /// </summary>
    public void StopBot()
    {
        isRunning = false;
        if (botCoroutine != null)
        {
            StopCoroutine(botCoroutine); // 停止协程
            botCoroutine = null;
        }
        GameController.Instance?.ClearSelectedTile(); // 清除可能残留的选中瓦片
        UpdateUI(); // 更新按钮文字
        Debug.Log("Bot 已停止");
    }

    /// <summary>
    /// 更新 Bot 按钮文字为 "Bot 演示"（停止状态）。
    /// </summary>
    private void UpdateUI()
    {
        if (UIManager.Instance != null)
            UIManager.Instance.UpdateBotButtonText(false);
    }

    /// <summary>
    /// Bot 主循环协程。
    /// 循环执行：等待空闲 → 找最优配对 → 点击消除 → 等待动画完成。
    /// 各步骤间有适当延迟，让玩家能观察操作过程。
    /// </summary>
    private IEnumerator BotLoop()
    {
        while (isRunning)
        {
            // 等待游戏处于空闲或胜利状态（动画中不可操作）
            yield return new WaitUntil(() =>
                GameController.Instance != null &&
                (GameController.Instance.CurrentState == GameState.Idle ||
                 GameController.Instance.CurrentState == GameState.Won));

            // 检查是否仍运行中（可能已被停止）
            if (!isRunning) break;

            // 游戏已胜利，停止 Bot
            if (GameController.Instance.CurrentState == GameState.Won)
            {
                StopBot();
                break;
            }

            // 寻找最优配对
            Tile tile1, tile2;
            bool found = FindBestPair(out tile1, out tile2);

            if (!found)
            {
                // 未找到配对：检查是否还有剩余瓦片
                GridManager grid = GridManager.Instance;
                if (grid != null && grid.HasRemainingTiles())
                {
                    // 有剩余但无可连通对：自动重新排列
                    grid.ShuffleRemainingTiles();
                    yield return new WaitForSeconds(0.5f); // 等待重排完成
                }
                else
                {
                    break; // 无剩余瓦片，退出
                }
                continue; // 重新开始循环
            }

            // 找到配对：依次点击两个瓦片
            GameController.Instance.OnTileSelected(tile1);
            yield return new WaitForSeconds(0.15f); // 第一个点击后短暂等待

            if (!isRunning) break; // 可能在等待时被停止

            GameController.Instance.OnTileSelected(tile2); // 第二个点击触发消除

            yield return new WaitForSeconds(0.4f); // 等待连线动画和消除完成
        }
    }

    /// <summary>
    /// 寻找最优配对。
    /// 扫描所有剩余瓦片，按类型分组，对每组内的两两组合检测连通性，
    /// 选择评分最低（拐角最少、距离最近）的配对。
    /// </summary>
    /// <param name="bestT1">输出的第一个瓦片</param>
    /// <param name="bestT2">输出的第二个瓦片</param>
    /// <returns>是否找到可配对</returns>
    private bool FindBestPair(out Tile bestT1, out Tile bestT2)
    {
        bestT1 = null;
        bestT2 = null;

        GridManager grid = GridManager.Instance;
        if (grid == null) return false;

        // 按类型分组所有剩余瓦片
        Dictionary<int, List<Tile>> groups = new Dictionary<int, List<Tile>>();

        for (int r = 1; r <= grid.Rows; r++)
        {
            for (int c = 1; c <= grid.Cols; c++)
            {
                if (grid.GridData[r, c] != null)
                {
                    int typeId = grid.GridData[r, c].Value;
                    Tile tile = grid.Tiles[r, c];
                    if (tile != null && !tile.IsEmpty)
                    {
                        // 将瓦片加入对应类型的组
                        if (!groups.ContainsKey(typeId))
                            groups[typeId] = new List<Tile>();
                        groups[typeId].Add(tile);
                    }
                }
            }
        }

        int bestScore = int.MaxValue; // 最优评分，越小越优

        // 遍历每个类型组
        foreach (var kvp in groups)
        {
            List<Tile> sameTypeTiles = kvp.Value;

            // 遍历组内的所有两两组合
            for (int i = 0; i < sameTypeTiles.Count; i++)
            {
                for (int j = i + 1; j < sameTypeTiles.Count; j++)
                {
                    Tile t1 = sameTypeTiles[i];
                    Tile t2 = sameTypeTiles[j];

                    PathFinder.Point p1 = new PathFinder.Point(t1.Row, t1.Col);
                    PathFinder.Point p2 = new PathFinder.Point(t2.Row, t2.Col);

                    // 检测连通性
                    List<PathFinder.Point> path = PathFinder.FindPath(
                        grid.GridData, grid.Rows, grid.Cols, p1, p2);

                    if (path != null)
                    {
                        // 可连通：计算评分
                        // 拐点数 = 路径点数 - 2（起点和终点不算拐点）
                        int turns = path.Count - 2;
                        // 曼哈顿距离
                        int dist = Mathf.Abs(t1.Row - t2.Row) + Mathf.Abs(t1.Col - t2.Col);
                        // 评分：拐角权重 100，距离权重 1（优先消除拐角少的、距离近的）
                        int score = turns * 100 + dist;

                        // 评分更低则更新最优配对
                        if (score < bestScore)
                        {
                            bestScore = score;
                            bestT1 = t1;
                            bestT2 = t2;
                        }
                    }
                }
            }
        }

        return bestT1 != null && bestT2 != null;
    }
}
