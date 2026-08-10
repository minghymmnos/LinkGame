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

    /// <summary>单例访问器</summary>
    public static GameController Instance => instance;
    /// <summary>当前游戏状态（只读），供 BotController 查询</summary>
    public GameState CurrentState => currentState;
    /// <summary>当前分数（只读）</summary>
    public int Score => score;
    /// <summary>游戏累计时间（只读）</summary>
    public float GameTime => gameTime;

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
        // 重置状态和数据
        currentState = GameState.Idle;
        firstSelectedTile = null;
        score = 0;
        gameTime = 0f;
        isTimerRunning = false;

        // 清除所有残留连线
        if (lineDrawer != null)
            lineDrawer.ClearAllLines();

        // 重新生成棋盘
        if (gridManager != null)
        {
            gridManager.InitializeGrid();
            pairsRemaining = (gridManager.Rows * gridManager.Cols) / 2; // 计算总对数
        }

        // 重置 UI 显示
        if (uiManager != null)
        {
            uiManager.UpdateScore(score);
            uiManager.UpdateTimer(gameTime);
            uiManager.ShowGameOver(false, 0f); // 隐藏过关面板
        }

        isTimerRunning = true; // 启动计时器
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
                    uiManager.UpdateScore(score);

                if (pairsRemaining <= 0)
                {
                    // 全部消除：胜利
                    isTimerRunning = false;
                    currentState = GameState.Won;
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
