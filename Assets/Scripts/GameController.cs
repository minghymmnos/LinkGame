using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 游戏状态枚举。
/// 定义了游戏在生命周期中的四种状态，用于状态机管理。
/// </summary>
public enum GameState
{
    Idle,       // 空闲：等待玩家点击第一个瓦片
    Selected,   // 已选中：已选中第一个瓦片，等待点击第二个
    Animating,  // 动画中：正在绘制连线/执行消除，禁止操作
    Won         // 已胜利：所有瓦片已消除，游戏结束
}

/// <summary>
/// 游戏控制器。
/// 管理游戏的核心状态流转、瓦片选中/配对消除逻辑、计分和计时。
/// 采用单例模式，全局唯一实例，供其他组件访问。
/// </summary>
public class GameController : MonoBehaviour
{
    // ---------- 依赖组件引用 ----------
    private GridManager gridManager;   // 网格管理器，提供网格数据和瓦片操作
    private LineDrawer lineDrawer;     // 连线绘制器，绘制配对连线
    private UIManager uiManager;       // UI 管理器，更新分数/计时/面板

    // ---------- 单例 ----------
    private static GameController instance;

    // ---------- 游戏状态 ----------
    private GameState currentState = GameState.Idle; // 当前游戏状态
    private Tile firstSelectedTile = null;            // 当前选中的第一个瓦片
    private int score = 0;                             // 当前分数
    private int pairsRemaining = 0;                    // 剩余待消除对数
    private float gameTime = 0f;                       // 游戏累计时间（秒）
    private bool isTimerRunning = false;               // 计时器是否运行中

    // ---------- 关卡设计器相关 ----------
    private string currentLevelId = null;              // 当前正在体验的关卡 ID（null=普通随机局）
    /// <summary>通关事件：参数为(levelId, clearTime_seconds)。UIManager 在 SetReferences 时订阅以记录通关时间。</summary>
    public event Action<string, float> OnLevelCleared;

    /// <summary>单例访问器</summary>
    public static GameController Instance => instance;
    /// <summary>当前游戏状态（只读），供 BotController 查询</summary>
    public GameState CurrentState => currentState;
    /// <summary>当前分数（只读）</summary>
    public int Score => score;
    /// <summary>游戏累计时间（只读）</summary>
    public float GameTime => gameTime;
    /// <summary>当前关卡 ID（只读），null 表示标准随机局</summary>
    public string CurrentLevelId => currentLevelId;

    private void Awake()
    {
        instance = this; // 设置单例
    }

    /// <summary>
    /// Unity 生命周期：脚本启动。
    /// 留空，游戏由 UIManager 的"开始游戏"按钮触发 StartNewGame()。
    /// </summary>
    private void Start()
    {
    }

    /// <summary>
    /// Unity 生命周期：每帧调用。
    /// 当计时器运行时，累加游戏时间并刷新 UI 显示。
    /// </summary>
    private void Update()
    {
        if (isTimerRunning)
        {
            gameTime += Time.deltaTime; // 累加帧时间
            if (uiManager != null)
                uiManager.UpdateTimer(gameTime); // 刷新计时显示
        }
    }

    /// <summary>
    /// 注入依赖组件。由 GameInitializer 在创建后调用。
    /// </summary>
    /// <param name="grid">网格管理器</param>
    /// <param name="line">连线绘制器</param>
    /// <param name="ui">UI 管理器</param>
    public void SetReferences(GridManager grid, LineDrawer line, UIManager ui)
    {
        gridManager = grid;
        lineDrawer = line;
        uiManager = ui;
    }

    /// <summary>
    /// 开始新游戏 / 重置游戏。
    /// 重置所有状态和数据，重新生成棋盘，启动计时器。
    /// 由"开始游戏"按钮或"重新开始"按钮触发。
    /// </summary>
    public void StartNewGame()
    {
        StartNewGame(null, null, null);
    }

    /// <summary>
    /// 扩展开始新游戏：启动一个 LevelInstance（关卡设计器生成的已保存关卡）
    /// 或按自定义参数启动一局（参数为 null 时使用默认）。
    /// </summary>
    /// <param name="level">指定已生成的关卡实例。若为 null 则按 override 参数启动标准局。</param>
    /// <param name="overrideRows">null=不覆盖，沿用 level.config.rows 或默认值</param>
    /// <param name="overrideCols">null=不覆盖，沿用 level.config.cols 或默认值</param>
    public void StartNewGame(LevelInstance level, int? overrideRows = null, int? overrideCols = null)
    {
        // ================================================================
        // 统一开头：所有模式都要重置状态（避免 level!=null 分支漏设 HUD/状态）
        // —— 严格沿用旧 GameState 枚举（Idle/Selected/Animating/Won），不引入额外字段
        // ================================================================
        currentState = GameState.Idle;
        firstSelectedTile = null;
        score = 0;
        gameTime = 0f;
        isTimerRunning = false;

        int?[,] snapshot = null;
        int r = -1, c = -1, t = -1;

        // —— 条件放宽：只要求 level != null 就必赋 currentLevelId（成绩记录不依赖 config 是否为空）
        //    旧条件 (level.config != null) 会导致 JSON 反序列化 config 为 null 时 currentLevelId 漏掉，
        //    通关时 OnLevelCleared 传 null → UIManager 不记录成绩（这是本次成绩不保存的根因）。
        if (level != null)
        {
            currentLevelId = level.levelId;
            if (level.config != null)
            {
                r = level.config.rows;
                c = level.config.cols;
                t = level.config.typeCount;
                snapshot = GridManager.RestoreSnapshot(level.gridSnapshot, r, c);
            }
            else
            {
                // config 为空的极端兜底：沿用整体 override 或默认；快照仍尝试加载 gridSnapshot
                Debug.LogWarning($"[GameController] StartNewGame: level.levelId='{level.levelId}' 的 config==null，已保留 currentLevelId 用于成绩记录，但尺寸/快照使用默认/覆盖值。");
                if (overrideRows.HasValue) r = overrideRows.Value;
                if (overrideCols.HasValue) c = overrideCols.Value;
                if (r > 0 && c > 0) snapshot = GridManager.RestoreSnapshot(level.gridSnapshot, r, c);
            }
        }
        else
        {
            currentLevelId = null;
            if (overrideRows.HasValue) r = overrideRows.Value;
            if (overrideCols.HasValue) c = overrideCols.Value;
        }

        // 清除所有残留连线
        if (lineDrawer != null) lineDrawer.ClearAllLines();

        // 重新生成棋盘（按快照 or 随机）
        if (gridManager != null) gridManager.InitializeGrid(snapshot, r, c, t, null);

        // ================================================================
        // 统一结尾：所有模式都同步 pairsRemaining / GameState 转 Idle(=开局可点) / HUD 刷新
        // —— 注意：旧状态机没有 Playing 枚举；Idle 才是「棋盘已就绪，等待玩家点第一个瓦片」的正确语义。
        // ================================================================
        int rawR = (gridManager != null) ? gridManager.Rows : (r > 0 ? r : 0);
        int rawC = (gridManager != null) ? gridManager.Cols : (c > 0 ? c : 0);
        int totalTiles = Mathf.Max(0, rawR * rawC);
        pairsRemaining = Mathf.Max(0, totalTiles / 2);

        // 计时器启动 = 游戏开始进入「可被点击消除」状态；isTimerRunning 是原始代码唯一的"进行中"标记
        isTimerRunning = true;
        currentState = GameState.Idle;

        // UI 更新：双保险（UIManager.Instance 优先，uiManager 字段兜底）
        var ui = UIManager.Instance != null ? UIManager.Instance : uiManager;
        if (ui != null)
        {
            ui.UpdateScore(score);
            ui.UpdateTimer(gameTime);
            ui.UpdatePairsRemaining(pairsRemaining);
            ui.UpdateBotButtonText(false);
            ui.HideDifficultyPanel();
            ui.ShowGameOver(false, 0f);
        }
        // 自定义关卡：缓存「当前关卡 id」→ 用户点「难度指标」时优先从 LevelInstance.metrics 读预计算快照
        if (UIManager.Instance != null)
            UIManager.Instance.CurrentMetricsLevelId = currentLevelId;

        Debug.Log($"[GameController] StartNewGame 完成：levelId={currentLevelId ?? "(随机局)"}, size={rawR}x{rawC}, pairs={pairsRemaining}, state={currentState}, isTimerRunning={isTimerRunning}");
    }

    /// <summary>
    /// 玩家点击 HUD「返回标题」时调用：终止当前局（不记通关、不删保存记录）。
    /// - 停止计时 / 重置 GameState=Idle / 清空高亮首瓦片 / 清连线
    /// - 通知 BotController 停止演示
    /// - 通知 UI 关闭 GameOver / Difficulty / 恢复 Bot 按钮文字显示为"Bot 演示"
    /// </summary>
    public void TerminateCurrentLevel()
    {
        if (!isTimerRunning && currentState == GameState.Idle && firstSelectedTile == null) return;
        isTimerRunning = false;
        currentState = GameState.Idle;
        if (firstSelectedTile != null)
        {
            firstSelectedTile.SetHighlight(false);
            firstSelectedTile = null;
        }
        score = 0;
        gameTime = 0f;
        pairsRemaining = 0;
        if (lineDrawer != null) lineDrawer.ClearAllLines();
        // BotController 不一定存在，用 null 判空即可（Unity 组件不可用/未初始化都视为 null）
        var bot = BotController.Instance;
        if (bot != null) bot.Stop();

        var ui = UIManager.Instance != null ? UIManager.Instance : uiManager;
        if (ui != null)
        {
            ui.ShowGameOver(false, 0f);
            ui.HideDifficultyPanel();
            ui.UpdateBotButtonText(false);
            ui.UpdateScore(0);
            ui.UpdateTimer(0f);
            ui.UpdatePairsRemaining(0);
        }
        Debug.Log($"[GameController] TerminateCurrentLevel：levelId={currentLevelId ?? "(随机局)"} 已终止并回到标题页。");
    }

    /// <summary>
    /// 瓦片选中处理。由 GridManager.OnTileClicked 转发，也是 BotController 调用的入口。
    /// 根据当前状态执行不同逻辑：选中第一个、配对检测或忽略。
    /// </summary>
    /// <param name="tile">被点击的瓦片</param>
    public void OnTileSelected(Tile tile)
    {
        // 动画中或已胜利时，忽略所有点击
        if (currentState == GameState.Animating || currentState == GameState.Won)
            return;

        if (currentState == GameState.Idle)
        {
            // 空闲状态：选中第一个瓦片
            firstSelectedTile = tile;
            tile.SetHighlight(true);      // 高亮显示
            currentState = GameState.Selected;
        }
        else if (currentState == GameState.Selected)
        {
            // 已选中状态：点击的是同一个瓦片，则取消选中
            if (tile == firstSelectedTile)
            {
                tile.SetHighlight(false);
                firstSelectedTile = null;
                currentState = GameState.Idle;
                return;
            }

            // 点击第二个瓦片，尝试配对消除
            tile.SetHighlight(true);
            TryMatch(firstSelectedTile, tile);
        }
    }

    /// <summary>
    /// 清除当前选中状态。取消第一个瓦片的高亮并回到空闲状态。
    /// 由 BotController 在停止时调用，防止残留选中。
    /// </summary>
    public void ClearSelectedTile()
    {
        if (currentState == GameState.Selected && firstSelectedTile != null)
        {
            firstSelectedTile.SetHighlight(false);
            firstSelectedTile = null;
            currentState = GameState.Idle;
        }
    }

    /// <summary>
    /// 尝试配对消除两个瓦片。
    /// 调用 PathFinder 检测连通性，若可连通则画线并消除。
    /// </summary>
    /// <param name="tile1">第一个瓦片</param>
    /// <param name="tile2">第二个瓦片</param>
    private void TryMatch(Tile tile1, Tile tile2)
    {
        currentState = GameState.Animating; // 进入动画状态，禁止其他操作

        // 构造路径起止点（网格坐标）
        PathFinder.Point start = new PathFinder.Point(tile1.Row, tile1.Col);
        PathFinder.Point end = new PathFinder.Point(tile2.Row, tile2.Col);

        // 调用寻路算法检测连通性
        List<PathFinder.Point> path = PathFinder.FindPath(
            gridManager.GridData, gridManager.Rows, gridManager.Cols, start, end);

        if (path != null)
        {
            // ---------- 可连通：执行消除 ----------
            tile1.SetHighlight(false);
            tile2.SetHighlight(false);

            // 绘制连线，延迟回调中执行消除
            lineDrawer.DrawPath(path, () =>
            {
                // 回调：连线显示完成后调用
                // 从网格中移除两个瓦片（设为空格）
                gridManager.RemoveTile(tile1.Row, tile1.Col);
                gridManager.RemoveTile(tile2.Row, tile2.Col);

                score += 10;        // 加分
                pairsRemaining--;   // 剩余对数减一

                if (uiManager != null)
                {
                    uiManager.UpdateScore(score);
                    uiManager.UpdatePairsRemaining(pairsRemaining);
                }
                // 双保险：若 UIManager.Instance 与字段不是同一对象（理论不会，但防御一下）
                if (UIManager.Instance != null && UIManager.Instance != uiManager)
                    UIManager.Instance.UpdatePairsRemaining(pairsRemaining);

                if (pairsRemaining <= 0)
                {
                    // 全部消除：胜利
                    isTimerRunning = false;
                    currentState = GameState.Won;
                    // 发布通关事件（含 levelId 和用时），供 UI/记录管理器订阅
                    try { OnLevelCleared?.Invoke(currentLevelId, gameTime); }
                    catch (System.Exception e) { Debug.LogError("OnLevelCleared 回调异常: " + e.Message); }
                    if (uiManager != null)
                        uiManager.ShowGameOver(true, gameTime); // 显示过关面板和用时
                }
                else if (!gridManager.HasValidMoves())
                {
                    // 还有剩余瓦片但无可消除对：自动重新排列
                    gridManager.ShuffleRemainingTiles();
                }

                currentState = GameState.Idle; // 回到空闲状态
            });
        }
        else
        {
            // ---------- 不可连通：取消选中 ----------
            tile1.SetHighlight(false);
            tile2.SetHighlight(false);
            firstSelectedTile = null;
            currentState = GameState.Idle;
        }
    }
}
