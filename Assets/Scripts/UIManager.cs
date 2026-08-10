using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI 管理器。
/// 负责管理所有 UI 元素的显示、更新和交互回调。
/// 管理 UI 层次结构：标题界面（TitlePanel）和游戏界面（GameHud）互斥显示。
/// 采用单例模式，供 GameController 和 BotController 调用更新接口。
/// </summary>
public class UIManager : MonoBehaviour
{
    private static UIManager instance;

    // ---------- 游戏 HUD 元素引用 ----------
    private Text scoreText;            // 分数文字（左上角）
    private Text timerText;            // 计时文字（右上角）
    private GameObject gameOverPanel;  // 过关面板（覆盖层）
    private Text gameOverText;         // 过关提示文字
    private Button restartButton;      // "重新开始" 按钮
    private Button shuffleButton;      // "重新排列" 按钮
    private Button overRestartButton;  // 过关面板 "再来一局" 按钮
    private Button botButton;          // "Bot 演示" 按钮
    private Text botButtonText;        // Bot 按钮文字（用于切换显示）

    // ---------- 标题界面元素引用 ----------
    private GameObject titlePanel;      // 标题面板容器
    private Button startButton;         // "开始游戏" 按钮
    private Button titleQuitButton;     // 标题界面 "结束游戏" 按钮
    private Button overQuitButton;      // 过关面板 "结束游戏" 按钮

    // ---------- 容器 ----------
    private GameObject gameHud;          // 游戏界面容器（含所有游戏中 UI）

    // ---------- 难度指标面板 ----------
    private GameObject difficultyPanel; // 难度指标面板
    private Text difficultyText;         // 难度指标文字
    private Button difficultyButton;      // 难度指标开关按钮

    /// <summary>单例访问器</summary>
    public static UIManager Instance => instance;

    private void Awake()
    {
        instance = this; // 设置单例
    }

    /// <summary>
    /// 接收所有 UI 引用并绑定按钮事件。
    /// 由 GameInitializer.SetupUI 在创建完所有 UI 对象后调用，类似依赖注入。
    /// </summary>
    /// <param name="score">分数文字</param>
    /// <param name="timer">计时文字</param>
    /// <param name="panel">过关面板</param>
    /// <param name="overText">过关提示文字</param>
    /// <param name="restart">重新开始按钮</param>
    /// <param name="shuffle">重新排列按钮</param>
    /// <param name="overRestart">再来一局按钮</param>
    /// <param name="bot">Bot 演示按钮</param>
    /// <param name="botText">Bot 按钮文字</param>
    /// <param name="titlePanel">标题面板</param>
    /// <param name="startBtn">开始游戏按钮</param>
    /// <param name="titleQuitBtn">标题退出按钮</param>
    /// <param name="overQuitBtn">过关退出按钮</param>
    /// <param name="gameHud">游戏界面容器</param>
    public void SetUIReferences(Text score, Text timer, GameObject panel, Text overText,
        Button restart, Button shuffle, Button overRestart, Button bot, Text botText,
        GameObject titlePanel, Button startBtn, Button titleQuitBtn, Button overQuitBtn, GameObject gameHud,
        GameObject diffPanel, Text diffText, Button diffBtn)
    {
        // 保存引用
        scoreText = score;
        timerText = timer;
        gameOverPanel = panel;
        gameOverText = overText;
        restartButton = restart;
        shuffleButton = shuffle;
        overRestartButton = overRestart;
        botButton = bot;
        botButtonText = botText;

        this.titlePanel = titlePanel;
        startButton = startBtn;
        titleQuitButton = titleQuitBtn;
        overQuitButton = overQuitBtn;
        this.gameHud = gameHud;

        difficultyPanel = diffPanel;
        difficultyText = diffText;
        difficultyButton = diffBtn;

        // 绑定所有按钮的点击事件
        if (restartButton != null)
            restartButton.onClick.AddListener(OnRestartClicked);

        if (shuffleButton != null)
            shuffleButton.onClick.AddListener(OnShuffleClicked);

        if (overRestartButton != null)
            overRestartButton.onClick.AddListener(OnRestartClicked);

        if (botButton != null)
            botButton.onClick.AddListener(OnBotClicked);

        if (startButton != null)
            startButton.onClick.AddListener(OnStartClicked);

        if (titleQuitButton != null)
            titleQuitButton.onClick.AddListener(OnQuitClicked);

        if (overQuitButton != null)
            overQuitButton.onClick.AddListener(OnQuitClicked);

        if (difficultyButton != null)
            difficultyButton.onClick.AddListener(OnDifficultyClicked);

        ShowTitle(true);              // 初始显示标题界面
        UpdateBotButtonText(false);   // 初始 Bot 按钮文字
        ShowDifficultyPanel(false);   // 初始隐藏难度面板
    }

    /// <summary>
    /// 切换标题界面和游戏界面的显示。
    /// 两者互斥：显示标题时隐藏游戏 HUD，反之亦然。
    /// </summary>
    /// <param name="show">是否显示标题界面</param>
    public void ShowTitle(bool show)
    {
        if (titlePanel != null)
            titlePanel.SetActive(show);
        if (gameHud != null)
            gameHud.SetActive(!show);
    }

    /// <summary>
    /// 更新分数显示。
    /// </summary>
    /// <param name="score">当前分数</param>
    public void UpdateScore(int score)
    {
        if (scoreText != null)
            scoreText.text = $"分数: {score}";
    }

    /// <summary>
    /// 更新计时显示，格式化为 MM:SS。
    /// </summary>
    /// <param name="time">游戏累计时间（秒）</param>
    public void UpdateTimer(float time)
    {
        if (timerText != null)
        {
            int minutes = Mathf.FloorToInt(time / 60);
            int seconds = Mathf.FloorToInt(time % 60);
            timerText.text = $"时间: {minutes:D2}:{seconds:D2}";
        }
    }

    /// <summary>
    /// 显示/隐藏过关面板。
    /// 显示时根据是否过关显示对应文字和用时。
    /// </summary>
    /// <param name="won">是否过关</param>
    /// <param name="finalTime">最终用时（秒）</param>
    public void ShowGameOver(bool won, float finalTime)
    {
        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(won);
            if (gameOverText != null)
            {
                int minutes = Mathf.FloorToInt(finalTime / 60);
                int seconds = Mathf.FloorToInt(finalTime % 60);
                gameOverText.text = won ? $"恭喜过关！\n用时 {minutes:D2}:{seconds:D2}" : "游戏结束";
            }
        }
    }

    /// <summary>
    /// 更新 Bot 按钮文字。
    /// 运行时显示 "停止 Bot"，停止时显示 "Bot 演示"。
    /// </summary>
    /// <param name="running">Bot 是否运行中</param>
    public void UpdateBotButtonText(bool running)
    {
        if (botButtonText != null)
            botButtonText.text = running ? "停止 Bot" : "Bot 演示";
    }

    /// <summary>
    /// "开始游戏" 按钮回调。
    /// 隐藏标题界面，开始新游戏。
    /// </summary>
    private void OnStartClicked()
    {
        ShowTitle(false);
        if (GameController.Instance != null)
            GameController.Instance.StartNewGame();
    }

    /// <summary>
    /// "重新开始" 按钮回调。
    /// 先停止 Bot（防止冲突），再开始新游戏。
    /// </summary>
    private void OnRestartClicked()
    {
        if (BotController.Instance != null)
            BotController.Instance.StopBot();
        if (GameController.Instance != null)
            GameController.Instance.StartNewGame();
    }

    /// <summary>
    /// "重新排列" 按钮回调。
    /// 打乱剩余瓦片的位置。
    /// </summary>
    private void OnShuffleClicked()
    {
        if (GridManager.Instance != null)
            GridManager.Instance.ShuffleRemainingTiles();
    }

    /// <summary>
    /// "Bot 演示" 按钮回调。
    /// 切换 Bot 启停状态，并更新按钮文字。
    /// </summary>
    private void OnBotClicked()
    {
        if (BotController.Instance != null)
        {
            BotController.Instance.ToggleBot();
            UpdateBotButtonText(BotController.Instance.IsRunning);
        }
    }

    /// <summary>
    /// "结束游戏" 按钮回调。
    /// 关闭应用程序。
    /// </summary>
    private void OnQuitClicked()
    {
        Application.Quit();
    }

    /// <summary>
    /// 显示/隐藏难度指标面板。
    /// </summary>
    /// <param name="show">是否显示</param>
    public void ShowDifficultyPanel(bool show)
    {
        if (difficultyPanel != null)
            difficultyPanel.SetActive(show);
    }

    /// <summary>
    /// 更新难度指标文字内容。
    /// </summary>
    /// <param name="text">格式化后的指标文本</param>
    public void UpdateDifficultyText(string text)
    {
        if (difficultyText != null)
            difficultyText.text = text;
    }

    /// <summary>
    /// "难度指标" 按钮回调。
    /// 切换面板显示状态，显示时触发指标计算。
    /// </summary>
    private void OnDifficultyClicked()
    {
        if (difficultyPanel == null) return;

        bool show = !difficultyPanel.activeSelf;
        ShowDifficultyPanel(show);

        if (show)
        {
            // 计算当前关卡难度指标并显示
            var metrics = DifficultyAnalyzer.Analyze();
            UpdateDifficultyText(DifficultyAnalyzer.FormatMetrics(metrics));
        }
    }
}
