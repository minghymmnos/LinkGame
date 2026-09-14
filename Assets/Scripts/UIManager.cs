using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections.Generic;

/// <summary>
/// UI 管理器。
/// 负责管理所有 UI 元素的显示、更新和交互回调。
/// 管理 UI 层次结构：标题界面（TitlePanel）/ 关卡设计 / 关卡列表 / 通关记录 互斥切换。
/// 采用单例模式，供 GameController 和 BotController 调用更新接口。
/// </summary>
public class UIManager : MonoBehaviour
{
    private static UIManager instance;

    // ---------- 游戏 HUD 元素引用 ----------
    private Text scoreText;            // 分数文字（左上角）
    private Text timerText;            // 计时文字（右上角）
    private Text pairsRemainingText;   // 剩余对数文字（顶部中间，可选：若为 null 则不显示）
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
    private GameObject gameHud;         // 游戏界面容器（含所有游戏中 UI）

    // ---------- 难度指标面板 ----------
    private GameObject difficultyPanel;
    private Text difficultyText;
    private Button difficultyButton;

    // ========== 关卡设计器相关引用 ==========
    private Button titleLevelDesignBtn;      // 标题页「关卡设计」按钮
    private GameObject levelDesignPanel;     // 关卡设计面板
    private InputField rowsInput, colsInput, typesInput, genCountInput;
    private Text[] metricLabels;             // 8 指标标签
    private InputField[] metricInputs;       // 8 指标数值输入框
    private Dropdown[] metricGradeDrops;     // 8 指标分级下拉
    private Toggle[] metricUseGradeToggles;  // 8 指标「按分级」Toggle
    private Text ddPreviewText;              // 综合 DD 预览文字
    private Button generateBtn;              // 生成关卡按钮（生成期间切换为「取消生成」）
    private Image _genProgressFill;          // 生成进度条：橙色填充图（FillMethod=Horizontal）
    private Text _genProgressLabel;          // 生成进度条：百分比/进度文字
    private GameObject _genProgressBar;      // 生成进度条：根容器（用于显隐）
    private Button[] metricBatchBtns;         // 批量操作：[0]全部按数值 [1]全部按分级 [2]重置默认
    private Text designMsgText;              // 独立的提示/校验/结果信息区（避免挤占 DD 预览）
    private bool _isGenerating;              // 是否正在生成关卡
    private bool _genCancelled;              // 生成过程是否被玩家取消
    private Button backToTitleBtn2;          // 关卡设计→返回标题
    private Button backToTitleBtnInGame;     // 常规游玩界面右上角「返回标题」，一键退出当前局回到标题页
    private Button recordsBtn;               // 打开记录面板按钮

    // 关卡列表面板
    private GameObject levelSelectorPanel;
    private GameObject levelListContainer;
    private Button backToDesignBtn;
    private Button backToTitleBtn3;
    private Button refreshListBtn;

    // 记录面板
    private GameObject recordPanel;
    private Button recordsCloseBtn;
    private Button recordBackBtn;
    private GameObject recordsListContainer;
    private Button recordDeleteBtn;
    private Text currentRecordLevelLabel;

    // 关卡指标报告面板（关卡列表卡片「指标报告」按钮打开：展示该关卡 8 指标实际值 vs 生成目标 + 偏差）
    private GameObject metricReportPanel;
    private Text metricReportTitleText;
    private Text metricReportBodyText;
    private Button metricReportBackBtn;

    // 当前在记录面板中被选中的记录索引
    private LevelInstance _currentRecordLevel;
    private int _selectedRecordIndex = -1;
    private readonly List<GameObject> _levelCardObjects = new List<GameObject>();
    private readonly List<GameObject> _recordEntryObjects = new List<GameObject>();
    // 记录排序模型：字段 + 升降序
    private enum RecSortField { Index, Duration, Date }
    private RecSortField _recSortField = RecSortField.Index;   // 默认按"通关序号（第几次通关）"升序
    private bool _recSortAsc = true;                           // true=升序 1..N 在上面，false=降序 N..1 在上面
    /// <summary>记录行排序中间数据：origIndex 保留原始第几次通关（排序后仍不乱）。</summary>
    private sealed class RecEntry { public int origIndex; public float duration; public string tsStr; public DateTime tsDt; }
    private readonly List<GameObject> _recordRowObjects = new List<GameObject>();  // 仅含"条目行根"，用于选中高亮（不含表头/子按钮/空态）
    private readonly List<int> _recordOrigIndexOrder = new List<int>();             // 同步排序后的 origIndex：order[显示位置] = origIndex，删除选中行时按此找到真实索引不删错

    /// <summary>HUD 右上返回按钮来源标记：true=从关卡列表「开始体验」进入，点击应返回关卡列表；false=从标题「开始游戏」进入，点击返回标题页。</summary>
    private bool _lastFromLevelList = false;

    /// <summary>GameController 启动某关卡后写入该值；用户点「难度指标」按钮时优先读取该 LevelInstance.metrics 预计算快照。</summary>
    public string CurrentMetricsLevelId { get; set; }

    /// <summary>单例访问器</summary>
    public static UIManager Instance => instance;

    private void Awake()
    {
        instance = this;
        _selectedRecordIndex = -1;
        _currentRecordLevel = null;
    }

    /// <summary>
    /// 接收所有 UI 引用并绑定按钮事件。
    /// 由 GameInitializer.SetupUI 在创建完所有 UI 对象后调用。
    /// </summary>
    public void SetUIReferences(
        Text score, Text timer, GameObject panel, Text overText,
        Button restart, Button shuffle, Button overRestart, Button bot, Text botText,
        GameObject titlePanel, Button startBtn, Button titleQuitBtn, Button overQuitBtn, GameObject gameHud,
        GameObject diffPanel, Text diffText, Button diffBtn,
        Button titleLDBtn,
        GameObject lvlDesignPanel,
        InputField rIn, InputField cIn, InputField tIn, InputField gIn,
        Text[] mLbls, InputField[] mIn, Dropdown[] mDrop, Toggle[] mTog,
        Text ddPrev, Button genBtn, Image genProgFill, Text genProgLabel, Button back2,
        GameObject lvlSelPanel, GameObject lvList, Button back2D, Button back2T, Button refreshBtn,
        GameObject recPanel, Button recBtn, Button recClose, Button recBack,
        GameObject recList, Button recDel, Text recLevelLabel,
        Text pairsText = null,
        Button hudBackToTitle = null,
        Button[] metricBatch = null,
        Text designMsg = null,
        GameObject metricPanel = null,
        Text metricTitle = null,
        Text metricBody = null,
        Button metricBack = null)
    {
        scoreText = score;
        timerText = timer;
        pairsRemainingText = pairsText;
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
        backToTitleBtnInGame = hudBackToTitle;

        // 新引用
        titleLevelDesignBtn = titleLDBtn;
        levelDesignPanel = lvlDesignPanel;
        rowsInput = rIn; colsInput = cIn; typesInput = tIn; genCountInput = gIn;
        metricLabels = mLbls; metricInputs = mIn; metricGradeDrops = mDrop; metricUseGradeToggles = mTog;
        ddPreviewText = ddPrev;
        generateBtn = genBtn;
        _genProgressFill = genProgFill;
        _genProgressLabel = genProgLabel;
        if (_genProgressFill != null) _genProgressBar = _genProgressFill.transform.parent.gameObject;
        if (_genProgressBar != null) _genProgressBar.SetActive(false);
        metricBatchBtns = metricBatch;
        designMsgText = designMsg;
        backToTitleBtn2 = back2;
        recordsBtn = recBtn;

        levelSelectorPanel = lvlSelPanel;
        levelListContainer = lvList;
        backToDesignBtn = back2D;
        backToTitleBtn3 = back2T;
        refreshListBtn = refreshBtn;

        recordPanel = recPanel;
        recordsCloseBtn = recClose;
        recordBackBtn = recBack;
        recordsListContainer = recList;
        recordDeleteBtn = recDel;
        currentRecordLevelLabel = recLevelLabel;

        metricReportPanel = metricPanel;
        metricReportTitleText = metricTitle;
        metricReportBodyText = metricBody;
        metricReportBackBtn = metricBack;
        if (metricReportPanel != null) metricReportPanel.SetActive(false);

        // ---------- 绑定基础按钮事件 ----------
        if (restartButton != null) restartButton.onClick.AddListener(OnRestartClicked);
        if (shuffleButton != null) shuffleButton.onClick.AddListener(OnShuffleClicked);
        if (overRestartButton != null) overRestartButton.onClick.AddListener(OnRestartClicked);
        if (botButton != null) botButton.onClick.AddListener(OnBotClicked);
        if (startButton != null) startButton.onClick.AddListener(OnStartClicked);
        if (titleQuitButton != null) titleQuitButton.onClick.AddListener(OnQuitClicked);
        if (overQuitButton != null) overQuitButton.onClick.AddListener(OnGameOverBackButtonClicked);
        if (difficultyButton != null) difficultyButton.onClick.AddListener(OnDifficultyClicked);
        if (backToTitleBtnInGame != null)
        {
            backToTitleBtnInGame.onClick.AddListener(OnInGameBackClicked);
        }

        // ---------- 关卡设计 & 记录面板 ----------
        if (titleLevelDesignBtn != null) titleLevelDesignBtn.onClick.AddListener(OnOpenLevelDesign);
        if (backToTitleBtn2 != null) backToTitleBtn2.onClick.AddListener(() => ShowTitle(true));
        if (backToDesignBtn != null) backToDesignBtn.onClick.AddListener(OnOpenLevelDesign);
        if (backToTitleBtn3 != null) backToTitleBtn3.onClick.AddListener(() => ShowTitle(true));
        if (refreshListBtn != null) refreshListBtn.onClick.AddListener(RebuildLevelList);
        if (generateBtn != null) generateBtn.onClick.AddListener(OnGenerateLevels);
        if (recordsBtn != null) recordsBtn.onClick.AddListener(() => ShowRecordPanel(null));
        if (recordsCloseBtn != null) recordsCloseBtn.onClick.AddListener(() => recordPanel.SetActive(false));
        if (recordBackBtn != null) recordBackBtn.onClick.AddListener(() => recordPanel.SetActive(false));
        if (recordDeleteBtn != null) recordDeleteBtn.onClick.AddListener(OnDeleteSelectedRecord);
        // 指标报告面板：返回按钮 = 关闭报告并回到关卡列表
        if (metricReportBackBtn != null) metricReportBackBtn.onClick.AddListener(OpenLevelSelector);

        // ---------- 8 指标双向联动绑定 ----------
        if (metricInputs != null && metricGradeDrops != null && metricUseGradeToggles != null)
        {
            for (int i = 0; i < 8; i++)
            {
                int idx = i; // 闭包
                if (metricInputs[idx] != null)
                {
                    metricInputs[idx].onValueChanged.AddListener(_ => OnMetricValueChanged(idx));
                }
                if (metricGradeDrops[idx] != null)
                {
                    metricGradeDrops[idx].onValueChanged.AddListener(_ => OnMetricGradeChanged(idx));
                }
                if (metricUseGradeToggles[idx] != null)
                {
                    metricUseGradeToggles[idx].onValueChanged.AddListener(_ => OnMetricToggleChanged(idx));
                }
            }
            // 初始状态：两种调节方式互斥（默认「按数值」模式，分级下拉锁定变暗）
            ApplyAllMetricModes();
        }

        // 基础参数变化时同步刷新 DD 预览 + 各指标的可达性标红（方案4）+ 实时参数校验
        if (rowsInput != null) rowsInput.onValueChanged.AddListener(_ => OnBaseParamChanged());
        if (colsInput != null) colsInput.onValueChanged.AddListener(_ => OnBaseParamChanged());
        if (typesInput != null) typesInput.onValueChanged.AddListener(_ => OnBaseParamChanged());
        if (genCountInput != null) genCountInput.onValueChanged.AddListener(_ => OnBaseParamChanged());

        // 指标批量操作按钮：全部按数值 / 全部按分级 / 重置默认
        if (metricBatchBtns != null)
        {
            if (metricBatchBtns.Length > 0 && metricBatchBtns[0] != null)
                metricBatchBtns[0].onClick.AddListener(() => SetAllMetricMode(false));
            if (metricBatchBtns.Length > 1 && metricBatchBtns[1] != null)
                metricBatchBtns[1].onClick.AddListener(() => SetAllMetricMode(true));
            if (metricBatchBtns.Length > 2 && metricBatchBtns[2] != null)
                metricBatchBtns[2].onClick.AddListener(ResetAllMetrics);
        }

        // 游戏通关事件：自动写记录
        if (GameController.Instance != null)
        {
            GameController.Instance.OnLevelCleared -= OnGameLevelCleared;
            GameController.Instance.OnLevelCleared += OnGameLevelCleared;
        }

        RefreshDDPreview();
        ShowTitle(true);
        UpdateBotButtonText(false);
        ShowDifficultyPanel(false);
    }

    /// <summary>
    /// 确保已订阅 GameController.OnLevelCleared（幂等，可安全重复调用）。
    /// —— 防御初始化时序问题：若 SetUIReferences 执行时 GameController 尚未创建（Instance==null），
    /// 上面的订阅会被跳过，通关事件将没有任何订阅者，导致通关记录永远写不进去（曾出现的 bug）。
    /// 因此每次真正进入游戏（开始游戏 / 再来一局 / 关卡列表开始体验）前都调用一次。
    /// </summary>
    private void EnsureLevelClearedSubscribed()
    {
        if (GameController.Instance == null) return;
        GameController.Instance.OnLevelCleared -= OnGameLevelCleared;
        GameController.Instance.OnLevelCleared += OnGameLevelCleared;
    }

    // ================================================================
    // 面板切换
    // ================================================================

    /// <summary>切换标题界面与其他面板的显隐。</summary>
    public void ShowTitle(bool show)
    {
        if (titlePanel != null) titlePanel.SetActive(show);
        if (gameHud != null) gameHud.SetActive(!show);
        if (levelDesignPanel != null) levelDesignPanel.SetActive(false);
        if (levelSelectorPanel != null) levelSelectorPanel.SetActive(false);
        if (recordPanel != null) recordPanel.SetActive(false);
        if (metricReportPanel != null) metricReportPanel.SetActive(false);
    }

    /// <summary>打开关卡设计面板（隐藏其他非游戏 HUD 面板）。</summary>
    private void OnOpenLevelDesign()
    {
        if (titlePanel != null) titlePanel.SetActive(false);
        if (gameHud != null) gameHud.SetActive(false);
        if (levelSelectorPanel != null) levelSelectorPanel.SetActive(false);
        if (recordPanel != null) recordPanel.SetActive(false);
        if (metricReportPanel != null) metricReportPanel.SetActive(false);
        if (levelDesignPanel != null) levelDesignPanel.SetActive(true);
        ApplyAllMetricModes();   // 重开面板时重新对齐互斥状态
        RefreshAllMetricLabels();
        RefreshDDPreview();
        SetDesignMessage(null, Color.white);   // 清空上一次的提示/校验信息
    }

    /// <summary>打开关卡列表面板。</summary>
    private void OpenLevelSelector()
    {
        if (titlePanel != null) titlePanel.SetActive(false);
        if (gameHud != null) gameHud.SetActive(false);
        if (levelDesignPanel != null) levelDesignPanel.SetActive(false);
        if (recordPanel != null) recordPanel.SetActive(false);
        if (metricReportPanel != null) metricReportPanel.SetActive(false);
        if (levelSelectorPanel != null) levelSelectorPanel.SetActive(true);
        RebuildLevelList();
    }

    /// <summary>
    /// 打开「关卡指标报告」面板：展示该关卡 8 个指标的**实际值 vs 生成时目标值 + 偏差**，
    /// 内容与生成关卡时控制台输出的日志一致（数据来自 LevelGenerator.GetMetricReport）。
    /// </summary>
    private void ShowMetricReport(string levelId)
    {
        if (metricReportPanel == null)
        {
            Debug.LogWarning("[ShowMetricReport] 指标报告面板未创建（GameInitializer 未传入 metricPanel），已忽略。");
            return;
        }
        if (LevelRecordManager.Instance == null) return;
        var lv = LevelRecordManager.Instance.FindLevel(levelId);
        if (lv == null)
        {
            Debug.LogWarning($"[ShowMetricReport] 找不到关卡：levelId={levelId}");
            return;
        }

        if (metricReportTitleText != null)
        {
            var g = DifficultyAnalyzer.NormalizedValueToGrade(lv.overallDD);
            string status = lv.generationTimedOut ? "   ⚠未完全达标" : "";
            metricReportTitleText.text =
                $"关卡 {lv.id}   尺寸 {lv.config.rows}×{lv.config.cols}   类型数 {lv.config.typeCount}   " +
                $"综合 DD {lv.overallDD:F3} ({DifficultyGradeUtil.ToName(g)}){status}";
        }
        if (metricReportBodyText != null)
            metricReportBodyText.text = LevelGenerator.GetMetricReport(lv);

        // 面板互斥显示：报告面板盖住关卡列表
        if (titlePanel != null) titlePanel.SetActive(false);
        if (gameHud != null) gameHud.SetActive(false);
        if (levelDesignPanel != null) levelDesignPanel.SetActive(false);
        if (recordPanel != null) recordPanel.SetActive(false);
        if (levelSelectorPanel != null) levelSelectorPanel.SetActive(false);
        metricReportPanel.SetActive(true);
    }

    /// <summary>
    /// 打开通关记录面板。若 levelId 为 null 则默认显示第一个有记录的关卡；
    /// 否则指定显示某关卡的记录。
    /// </summary>
    public void ShowRecordPanel(string levelId)
    {
        if (recordPanel == null) return;
        recordPanel.SetActive(true);

        if (LevelRecordManager.Instance == null) return;
        var list = LevelRecordManager.Instance.GetLevels();
        LevelInstance target = null;
        if (!string.IsNullOrEmpty(levelId))
        {
            target = LevelRecordManager.Instance.FindLevel(levelId);
        }
        if (target == null)
        {
            foreach (var l in list)
            {
                if (l.clearRecords != null && l.clearRecords.Count > 0) { target = l; break; }
            }
            if (target == null && list.Count > 0) target = list[0];
        }
        _currentRecordLevel = target;
        _selectedRecordIndex = -1;
        // 验证日志：确认打开记录面板时数据是否真的存在（区分"数据没存上"与"UI 没显示"）
        if (target != null)
        {
            int cnt = (target.clearRecords != null) ? target.clearRecords.Count : 0;
            Debug.Log($"[Record] 打开记录面板：关卡 id='{target.id}'，记录数={cnt}，bestTime={target.bestTime:F2}s");
        }
        else
        {
            Debug.LogWarning("[Record] 打开记录面板：未找到任何关卡（levelId=" + (levelId ?? "null") + "）。");
        }
        RebuildRecordList();
    }

    // ================================================================
    // 基础 HUD 更新
    // ================================================================

    public void UpdateScore(int score) { if (scoreText != null) scoreText.text = $"分数: {score}"; }

    /// <summary>更新剩余对数显示。若 HUD 中未创建 pairsRemainingText 则静默忽略。</summary>
    public void UpdatePairsRemaining(int pairs)
    {
        if (pairsRemainingText != null) pairsRemainingText.text = $"剩余对数: {Mathf.Max(0, pairs)}";
    }

    public void UpdateTimer(float time)
    {
        if (timerText == null) return;
        int minutes = Mathf.FloorToInt(time / 60);
        int seconds = Mathf.FloorToInt(time % 60);
        timerText.text = $"时间: {minutes:D2}:{seconds:D2}";
    }

    public void ShowGameOver(bool won, float finalTime)
    {
        if (gameOverPanel == null) return;
        // 根据本局进入来源同步按钮布局：
        // - 从关卡列表「开始体验」进入 → 通关后只显示一个「返回关卡列表」橙色按钮（隐藏"再来一局"）
        // - 从标题页「开始游戏」进入 → 通关后显示「再来一局 + 返回标题」两按钮（常规）
        SyncGameOverPanelForSource();
        gameOverPanel.SetActive(won);
        if (gameOverText != null)
        {
            int minutes = Mathf.FloorToInt(finalTime / 60);
            int seconds = Mathf.FloorToInt(finalTime % 60);
            gameOverText.text = won ? $"恭喜过关！\n用时 {minutes:D2}:{seconds:D2}" : "游戏结束";
        }
    }

    /// <summary>
    /// 根据 _lastFromLevelList 同步 GameOver 面板按钮布局：
    /// - 关卡列表来源通关：隐藏 overRestartButton；右下键变为文字「返回关卡列表」+ 橙色（主按钮）；
    /// - 标题页常规通关：显示 overRestartButton；右下键为文字「返回标题」+ 蓝灰（两按钮）。
    /// </summary>
    private void SyncGameOverPanelForSource()
    {
        bool fromList = _lastFromLevelList;
        if (overRestartButton != null) overRestartButton.gameObject.SetActive(!fromList);
        if (overQuitButton != null)
        {
            Text label = overQuitButton.GetComponentInChildren<Text>();
            Image img = overQuitButton.GetComponent<Image>();
            if (fromList)
            {
                if (label != null) label.text = "返回关卡列表";
                if (img != null) img.color = new Color(0.92f, 0.55f, 0.15f);   // 橙色强调：主操作唯一按钮
            }
            else
            {
                if (label != null) label.text = "返回标题";
                if (img != null) img.color = new Color(0.30f, 0.50f, 0.75f);   // 蓝灰色：次级返回按钮
            }
        }
    }

    public void UpdateBotButtonText(bool running)
    {
        if (botButtonText != null) botButtonText.text = running ? "停止 Bot" : "Bot 演示";
    }

    public void ShowDifficultyPanel(bool show)
    {
        if (difficultyPanel != null) difficultyPanel.SetActive(show);
    }

    /// <summary>快捷隐藏难度面板（等价于 ShowDifficultyPanel(false)）。供 GameController.StartNewGame 初始化时统一调用。</summary>
    public void HideDifficultyPanel() { ShowDifficultyPanel(false); }

    public void UpdateDifficultyText(string text)
    {
        if (difficultyText != null) difficultyText.text = text;
    }

    // ================================================================
    // 按钮回调
    // ================================================================

    private void OnStartClicked()
    {
        EnsureLevelClearedSubscribed();   // 防御：确保通关事件订阅存在（防初始化时序问题）
        ShowTitle(false);
        _lastFromLevelList = false;     // 标题页启动：HUD 右上显示「返回标题」
        SyncHudBackButtonText();
        if (GameController.Instance != null) GameController.Instance.StartNewGame();
    }

    private void OnRestartClicked()
    {
        EnsureLevelClearedSubscribed();
        if (BotController.Instance != null) BotController.Instance.StopBot();
        if (GameController.Instance != null) GameController.Instance.StartNewGame();
    }

    private void OnShuffleClicked()
    {
        if (GridManager.Instance != null) GridManager.Instance.ShuffleRemainingTiles();
    }

    private void OnBotClicked()
    {
        if (BotController.Instance == null) return;
        BotController.Instance.ToggleBot();
        UpdateBotButtonText(BotController.Instance.IsRunning);
    }

    private void OnQuitClicked() { Application.Quit(); }

    /// <summary>
    /// HUD 右上返回按钮：根据进入来源决定返回目标。
    /// - _lastFromLevelList=true  → 玩家从关卡列表「开始体验」进入 → 回关卡列表面板并刷新
    /// - _lastFromLevelList=false → 玩家从标题页「开始游戏」进入 → 回标题页
    /// </summary>
    private void OnInGameBackClicked()
    {
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
        if (difficultyPanel != null) difficultyPanel.SetActive(false);
        if (GameController.Instance != null)
            GameController.Instance.TerminateCurrentLevel();

        if (_lastFromLevelList)
        {
            Debug.Log("[UIManager] 玩家点击 HUD「返回关卡列表」：从自定义关卡回到生成关卡列表。");
            _lastFromLevelList = false;
            SyncHudBackButtonText();
            OpenLevelSelector();
        }
        else
        {
            Debug.Log("[UIManager] 玩家点击 HUD「返回标题」：终止当前局并回到标题页。");
            ShowTitle(true);
        }
    }

    /// <summary>同步 HUD 右上返回按钮的文字。根据 _lastFromLevelList 切换「返回关卡列表」/「返回标题」。</summary>
    private void SyncHudBackButtonText()
    {
        if (backToTitleBtnInGame == null) return;
        Text label = backToTitleBtnInGame.GetComponentInChildren<Text>();
        if (label == null) return;
        label.text = _lastFromLevelList ? "返回关卡列表" : "返回标题";
    }

    /// <summary>
    /// GameOver 胜利面板上的返回按钮（分派）：
    /// - 关卡列表来源通关 → 终止当前局 + 关掉胜利面板 + 回到关卡列表面板（自动刷新列表，显示刚存的通关记录）
    /// - 常规标题页通关 → 终止当前局 + 关掉胜利面板 + 回到标题页。
    /// </summary>
    private void OnGameOverBackButtonClicked()
    {
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
        if (difficultyPanel != null) difficultyPanel.SetActive(false);
        if (GameController.Instance != null)
            GameController.Instance.TerminateCurrentLevel();

        if (_lastFromLevelList)
        {
            Debug.Log("[UIManager] 玩家点击通关面板「返回关卡列表」：自定义关卡通关，回到生成关卡列表。");
            _lastFromLevelList = false;
            SyncHudBackButtonText();
            OpenLevelSelector();
        }
        else
        {
            Debug.Log("[UIManager] 玩家点击通关面板「返回标题」，已回到标题页。");
            ShowTitle(true);
        }
    }

    [Obsolete("请使用 OnGameOverBackButtonClicked 分派方法。此方法保留仅作为历史兼容，不会被任何按钮直接绑定。")]
    private void OnGameOverBackToTitle() => OnGameOverBackButtonClicked();

    private void OnDifficultyClicked()
    {
        if (difficultyPanel == null) return;
        bool show = !difficultyPanel.activeSelf;
        ShowDifficultyPanel(show);
        if (show)
        {
            // 自定义关卡：优先使用 GameController 启动时写入的 CurrentMetricsLevelId
            // 对应的 LevelInstance.metrics 预计算快照（不受中途消除影响，权威且不耗时）
            string formatted;
            string keyId = CurrentMetricsLevelId;
            if (string.IsNullOrEmpty(keyId) && GameController.Instance != null)
                keyId = GameController.Instance.CurrentLevelId;

            if (!string.IsNullOrEmpty(keyId) && LevelRecordManager.Instance != null)
            {
                var lv = LevelRecordManager.Instance.FindLevel(keyId);
                if (lv != null && lv.metrics != null)
                {
                    float[] raw = DifficultyAnalyzer.LastRawValues;
                    formatted = DifficultyAnalyzer.FormatMetrics(lv.metrics, raw);
                    UpdateDifficultyText(formatted);
                    return;
                }
            }
            var metrics = DifficultyAnalyzer.Analyze();
            formatted = DifficultyAnalyzer.FormatMetrics(metrics);
            UpdateDifficultyText(formatted);
        }
    }

    // ================================================================
    // 难度指标双向联动 + DD 实时预览
    // ================================================================

    /// <summary>读取当前输入框的值并夹到 [0,1]。</summary>
    private float GetMetricNormValue(int i)
    {
        if (metricInputs == null || metricInputs[i] == null) return 0.5f;
        if (float.TryParse(metricInputs[i].text, out float v))
            return Mathf.Clamp(v, 0f, 1f);
        return 0.5f;
    }

    /// <summary>根据值反查所属分级并更新 CurGrade 文本显示。</summary>
    private void UpdateCurGradeLabel(int i, float v)
    {
        Transform t = metricInputs[i]?.transform?.parent;
        if (t == null) return;
        // 查找 LevelDesignPanel 下的 CurGrade_i 文本
        Transform panelT = levelDesignPanel?.transform;
        if (panelT == null) return;
        Transform lab = panelT.Find($"CurGrade_{i}");
        if (lab != null)
        {
            Text txt = lab.GetComponent<Text>();
            if (txt != null)
            {
                var g = DifficultyAnalyzer.NormalizedValueToGrade(v);
                // 方案4：显式设置但当前尺寸/类型数下不可达 → 标红提示
                bool unreachable = IsMetricExplicit(i) && !IsMetricReachable(i);
                if (unreachable)
                {
                    txt.text = DifficultyGradeUtil.ToName(g) + " ✕不可达";
                    txt.color = new Color(0.95f, 0.30f, 0.30f, 1f);
                }
                else
                {
                    txt.text = DifficultyGradeUtil.ToName(g);
                    Color c = DifficultyGradeUtil.ToColor(g);
                    txt.color = new Color(c.r, c.g, c.b, 1f);
                }
            }
        }
    }

    /// <summary>刷新全部 8 个指标的 CurGrade 标签（含"✕不可达"标红，方案4）。行列/类型数变化时调用。</summary>
    private void RefreshAllMetricLabels()
    {
        if (metricInputs == null) return;
        for (int i = 0; i < 8 && i < metricInputs.Length; i++)
        {
            if (metricInputs[i] == null) continue;
            UpdateCurGradeLabel(i, GetMetricNormValue(i));
        }
    }

    /// <summary>读取当前 UI 的基础参数（行/列/类型数，已做范围钳制，与 BuildConfigFromUI 一致）。</summary>
    private void GetCurrentGridParams(out int R, out int C, out int T)
    {
        if (!int.TryParse(rowsInput?.text ?? "", out R)) R = 8;
        if (!int.TryParse(colsInput?.text ?? "", out C)) C = 10;
        if (!int.TryParse(typesInput?.text ?? "", out T)) T = 8;
        R = Mathf.Max(2, Mathf.Min(20, R));
        C = Mathf.Max(2, Mathf.Min(20, C));
        T = Mathf.Max(2, Mathf.Min(64, T));
    }

    /// <summary>判断第 i 个指标是否为"显式设置"（开启按分级，或数值偏离默认 0.5）。</summary>
    private bool IsMetricExplicit(int i)
    {
        bool useGrade = metricUseGradeToggles != null && metricUseGradeToggles[i] != null && metricUseGradeToggles[i].isOn;
        if (useGrade) return true;
        return Mathf.Abs(GetMetricNormValue(i) - 0.5f) > 0.001f;
    }

    /// <summary>判断第 i 个指标的当前设置（按分级=等级区间；按数值=精确值）在当前尺寸/类型数下是否可达（方案4）。</summary>
    private bool IsMetricReachable(int i)
    {
        GetCurrentGridParams(out int R, out int C, out int T);
        float[,] ranges = LevelGenerator.EstimateReachableRanges(R, C, T);
        if (ranges == null) return true;

        float lo, hi;
        bool useGrade = metricUseGradeToggles != null && metricUseGradeToggles[i] != null && metricUseGradeToggles[i].isOn;
        if (useGrade)
        {
            int gi = Mathf.Clamp(metricGradeDrops[i] != null ? metricGradeDrops[i].value : 0, 0, 4);
            lo = DifficultyGradeUtil.GradeLower[gi];
            hi = (gi == 4) ? 1f : DifficultyGradeUtil.GradeUpper[gi];
        }
        else
        {
            lo = hi = GetMetricNormValue(i);
        }
        const float eps = 0.02f;
        return (hi + eps) >= ranges[i, 0] && (lo - eps) <= ranges[i, 1];
    }

    /// <summary>指标数值输入框值变化 → 更新分级显示 → 刷新综合 DD。</summary>
    private void OnMetricValueChanged(int i)
    {
        if (metricUseGradeToggles == null || metricUseGradeToggles[i] == null) return;
        if (metricUseGradeToggles[i].isOn) return; // 分级模式：不干涉
        float v = GetMetricNormValue(i);
        UpdateCurGradeLabel(i, v);
        RefreshDDPreview();
    }

    /// <summary>按分级下拉改变 → 在对应分级内随机生成新值 → 更新输入框 & CurGrade → 刷新 DD。</summary>
    private void OnMetricGradeChanged(int i)
    {
        if (metricUseGradeToggles == null || metricUseGradeToggles[i] == null) return;
        if (!metricUseGradeToggles[i].isOn) return; // 数值模式：不干涉
        var g = (DifficultyGrade)metricGradeDrops[i].value;
        float v = DifficultyAnalyzer.GradeToRandomNormalized(g);
        if (metricInputs[i] != null) metricInputs[i].text = FormatNorm(v);
        UpdateCurGradeLabel(i, v);
        RefreshDDPreview();
    }

    /// <summary>设置 Graphic 的透明度（保留原有 RGB）。</summary>
    private static void SetGraphicAlpha(Graphic g, float a)
    {
        if (g == null) return;
        Color c = g.color;
        c.a = a;
        g.color = c;
    }

    /// <summary>
    /// 归一化指标值的显示格式：最多 3 位小数并去掉无意义的尾随 0（0.5 而不是 0.500，1.0 保留一位小数）。
    /// </summary>
    private static string FormatNorm(float v)
    {
        return v.ToString("0.0##");
    }

    /// <summary>
    /// 应用第 i 个指标的调节方式（方案：两种调节方式互斥）。
    /// useGrade=true → 启用「按分级」下拉、锁定「归一化值」输入框；
    /// useGrade=false → 反之。被锁定的一方同时降低透明度，直观表明当前生效的方式。
    /// </summary>
    private void ApplyMetricMode(int i, bool useGrade)
    {
        if (metricInputs == null || metricGradeDrops == null) return;
        if (i < 0 || i >= metricInputs.Length || i >= metricGradeDrops.Length) return;

        // 归一化值输入框：分级模式下禁用并变暗
        InputField input = metricInputs[i];
        if (input != null)
        {
            // 关闭 Selectable 自带的 disabled 着色，避免与下方显式透明度叠加导致过暗
            ColorBlock icb = input.colors; icb.disabledColor = Color.white; input.colors = icb;
            input.interactable = !useGrade;
            SetGraphicAlpha(input.GetComponent<Image>(), useGrade ? 0.35f : 0.95f);
            SetGraphicAlpha(input.textComponent, useGrade ? 0.35f : 1f);
            SetGraphicAlpha(input.placeholder, useGrade ? 0.25f : 0.4f);
        }

        // 分级下拉框：数值模式下禁用并变暗
        Dropdown drop = metricGradeDrops[i];
        if (drop != null)
        {
            ColorBlock dcb = drop.colors; dcb.disabledColor = Color.white; drop.colors = dcb;
            drop.interactable = useGrade;
            SetGraphicAlpha(drop.GetComponent<Image>(), useGrade ? 0.95f : 0.35f);
            SetGraphicAlpha(drop.captionText, useGrade ? 1f : 0.35f);
            Transform arrow = drop.transform.Find("Arrow");
            if (arrow != null) SetGraphicAlpha(arrow.GetComponent<Graphic>(), useGrade ? 1f : 0.25f);
        }
    }

    /// <summary>按各指标 Toggle 的当前状态，批量应用互斥的调节方式（初始化 / 重开面板时调用）。</summary>
    private void ApplyAllMetricModes()
    {
        if (metricUseGradeToggles == null) return;
        for (int i = 0; i < 8 && i < metricUseGradeToggles.Length; i++)
        {
            bool on = metricUseGradeToggles[i] != null && metricUseGradeToggles[i].isOn;
            ApplyMetricMode(i, on);
        }
    }

    /// <summary>批量把 8 个指标切换为「按数值」（useGrade=false）或「按分级」（useGrade=true）。</summary>
    private void SetAllMetricMode(bool useGrade)
    {
        if (metricUseGradeToggles == null) return;
        for (int i = 0; i < 8 && i < metricUseGradeToggles.Length; i++)
        {
            // 仅在值发生变化时才会触发 OnValueChanged，因此循环后统一再套用一次
            if (metricUseGradeToggles[i] != null) metricUseGradeToggles[i].isOn = useGrade;
        }
        ApplyAllMetricModes();
        RefreshAllMetricLabels();
        RefreshDDPreview();
        SetDesignMessage(useGrade
            ? "已将 8 个指标全部切换为「按分级」（下拉在各自分级区间内随机取值）。"
            : "已将 8 个指标全部切换为「按数值」（可手动输入 0~1）。",
            new Color(0.60f, 0.85f, 1f));
    }

    /// <summary>把 8 个指标重置为默认：「按数值」模式 + 0.5。</summary>
    private void ResetAllMetrics()
    {
        if (metricInputs == null || metricUseGradeToggles == null) return;
        for (int i = 0; i < 8; i++)
        {
            if (metricUseGradeToggles[i] != null) metricUseGradeToggles[i].isOn = false;
            if (metricGradeDrops != null && i < metricGradeDrops.Length && metricGradeDrops[i] != null)
                metricGradeDrops[i].value = 0;
            if (metricInputs[i] != null) metricInputs[i].text = "0.5";
        }
        ApplyAllMetricModes();
        RefreshAllMetricLabels();
        RefreshDDPreview();
        SetDesignMessage("已将 8 个指标重置为默认（按数值 0.5）。", new Color(0.60f, 0.85f, 1f));
    }

    /// <summary>写入独立的提示/校验/结果信息区（msg 为空表示清空）。</summary>
    private void SetDesignMessage(string msg, Color color)
    {
        if (designMsgText == null) return;
        designMsgText.text = string.IsNullOrEmpty(msg) ? "" : msg;
        designMsgText.color = color;
    }

    /// <summary>把输入框文字标红（参数非法提示）。</summary>
    private static void MarkInputInvalid(InputField f)
    {
        if (f != null && f.textComponent != null) f.textComponent.color = new Color(1f, 0.45f, 0.45f);
    }

    /// <summary>恢复输入框文字为白色（参数合法）。</summary>
    private static void MarkInputValid(InputField f)
    {
        if (f != null && f.textComponent != null) f.textComponent.color = Color.white;
    }

    /// <summary>
    /// 校验基础参数是否合法（行 2~20 / 列 2~20 / 类型 2~64 / 数量 1~50，且行×列为偶数）。
    /// </summary>
    /// <param name="live">true = 输入过程中的实时校验：解析失败不报错（避免边输边弹提示）</param>
    private bool ValidateBaseParams(out string err, bool live = false)
    {
        err = null;
        MarkInputValid(rowsInput); MarkInputValid(colsInput); MarkInputValid(typesInput); MarkInputValid(genCountInput);

        if (!int.TryParse(rowsInput?.text ?? "", out int R))
        { if (live) return true; MarkInputInvalid(rowsInput); err = "行数必须是数字。"; return false; }
        if (!int.TryParse(colsInput?.text ?? "", out int C))
        { if (live) return true; MarkInputInvalid(colsInput); err = "列数必须是数字。"; return false; }
        if (!int.TryParse(typesInput?.text ?? "", out int T))
        { if (live) return true; MarkInputInvalid(typesInput); err = "配对类型数必须是数字。"; return false; }
        if (!int.TryParse(genCountInput?.text ?? "", out int N))
        { if (live) return true; MarkInputInvalid(genCountInput); err = "生成数量必须是数字。"; return false; }

        if (R < 2 || R > 20) { MarkInputInvalid(rowsInput); err = $"行数需在 2~20 之间（当前 {R}）。"; return false; }
        if (C < 2 || C > 20) { MarkInputInvalid(colsInput); err = $"列数需在 2~20 之间（当前 {C}）。"; return false; }
        if (T < 2 || T > 64) { MarkInputInvalid(typesInput); err = $"配对类型数需在 2~64 之间（当前 {T}）。"; return false; }
        if (N < 1 || N > 50) { MarkInputInvalid(genCountInput); err = $"生成数量需在 1~50 之间（当前 {N}）。"; return false; }
        if ((R * C) % 2 != 0)
        {
            MarkInputInvalid(rowsInput); MarkInputInvalid(colsInput);
            err = $"行数×列数必须为偶数（当前 {R}×{C}={R * C}），否则无法两两配对。";
            return false;
        }
        return true;
    }

    /// <summary>基础参数变化：刷新 DD 预览与可达性标红，并做实时合法性校验。</summary>
    private void OnBaseParamChanged()
    {
        RefreshDDPreview();
        RefreshAllMetricLabels();
        SetDesignMessage(null, Color.white); // 先清空，再按校验结果重新写入
        string err;
        if (!ValidateBaseParams(out err, live: true) && !string.IsNullOrEmpty(err))
            SetDesignMessage("⚠ " + err, new Color(0.95f, 0.62f, 0.25f));
    }

    /// <summary>切换生成按钮的形态：生成中显示红色「取消生成」，否则显示橙色「生成关卡」。</summary>
    private void SetGenerateButtonState(bool generating)
    {
        if (generateBtn == null) return;
        Text label = generateBtn.GetComponentInChildren<Text>();
        if (label != null) label.text = generating ? "取消生成" : "生成关卡";
        Image img = generateBtn.GetComponent<Image>();
        if (img != null) img.color = generating ? new Color(0.85f, 0.33f, 0.28f) : new Color(0.9f, 0.55f, 0.15f);
        generateBtn.interactable = true; // 生成期间仍可点击（用于取消）
    }

    /// <summary>按分级 Toggle 切换：启用分级模式时立即按当前下拉生成值。</summary>
    private void OnMetricToggleChanged(int i)
    {
        bool on = metricUseGradeToggles[i].isOn;
        ApplyMetricMode(i, on);   // 互斥：启用一方即锁定另一方
        if (on)
        {
            var g = (DifficultyGrade)metricGradeDrops[i].value;
            float v = DifficultyAnalyzer.GradeToRandomNormalized(g);
            if (metricInputs[i] != null) metricInputs[i].text = FormatNorm(v);
            UpdateCurGradeLabel(i, v);
        }
        else
        {
            float v = GetMetricNormValue(i);
            UpdateCurGradeLabel(i, v);
        }
        RefreshDDPreview();
    }

    /// <summary>读取当前 UI 为 LevelConfig（不调用 SaveAll，仅内存对象）。</summary>
    private LevelConfig BuildConfigFromUI(System.Random rng = null)
    {
        var cfg = new LevelConfig();
        if (!int.TryParse(rowsInput?.text ?? "", out int R)) R = 8;
        if (!int.TryParse(colsInput?.text ?? "", out int C)) C = 10;
        if (!int.TryParse(typesInput?.text ?? "", out int T)) T = 8;
        cfg.rows = Mathf.Max(2, Mathf.Min(20, R));
        cfg.cols = Mathf.Max(2, Mathf.Min(20, C));
        cfg.typeCount = Mathf.Max(2, Mathf.Min(64, T));

        for (int i = 0; i < 8; i++)
        {
            bool useGrade = metricUseGradeToggles != null && metricUseGradeToggles[i] != null && metricUseGradeToggles[i].isOn;
            if (useGrade)
            {
                var g = (DifficultyGrade)metricGradeDrops[i].value;
                cfg.metrics[i] = new MetricConstraint
                {
                    useGrade = true,
                    grade = g,
                    normalizedValue = DifficultyAnalyzer.GradeToRandomNormalized(g, rng)
                };
            }
            else
            {
                float v = GetMetricNormValue(i);
                cfg.metrics[i] = new MetricConstraint
                {
                    useGrade = false,
                    grade = DifficultyAnalyzer.NormalizedValueToGrade(v),
                    normalizedValue = v
                };
            }
        }
        return cfg;
    }

    /// <summary>根据当前 UI 输入，实时计算综合 DD 并刷新预览文本；同时提示不可达指标数量（方案4）。</summary>
    private void RefreshDDPreview()
    {
        if (ddPreviewText == null) return;
        try
        {
            var cfg = BuildConfigFromUI();
            float[] norms = DifficultyAnalyzer.ResolveTargetNorms(cfg);
            float dd = DifficultyAnalyzer.PredictOverallDD(cfg.rows, cfg.cols, cfg.typeCount, norms);
            var g = DifficultyAnalyzer.NormalizedValueToGrade(dd);

            // 方案4：统计"显式设置但当前尺寸/类型数下不可达"的指标数量
            int badCount = LevelGenerator.CountUnreachableExplicit(cfg);
            string warn = badCount > 0 ? $"   ⚠{badCount}项不可达" : "";
            ddPreviewText.text = $"{dd:F3}  ({DifficultyGradeUtil.ToName(g)}){warn}";

            if (badCount > 0)
            {
                ddPreviewText.color = new Color(0.95f, 0.75f, 0.20f); // 橙黄警告
            }
            else
            {
                var gc = DifficultyGradeUtil.ToColor(g);
                ddPreviewText.color = new Color(gc.r, gc.g, gc.b, 1f);
            }
        }
        catch (Exception e)
        {
            ddPreviewText.text = $"(参数异常) {e.Message}";
            ddPreviewText.color = Color.red;
        }
    }

    // ================================================================
    // 生成关卡 + 关卡列表渲染
    // ================================================================

    /// <summary>
    /// 「生成关卡」按钮：先做可行性预检（方案4），不可达则拦截并提示；
    /// 通过后启动协程 CoGenerateLevels 逐关生成 + 动态进度条（非阻塞 UI）。
    /// —— 生成过程中再次点击该按钮 = 取消生成（保留已生成的关卡）。
    /// </summary>
    private void OnGenerateLevels()
    {
        // 生成中：本次点击视为「取消生成」
        if (_isGenerating)
        {
            _genCancelled = true;
            Debug.Log("[UIManager] 玩家点击「取消生成」，将在当前关卡生成完成后停止。");
            SetDesignMessage("正在取消生成…（当前关卡完成后停止，已生成关卡会保留）", new Color(0.95f, 0.75f, 0.20f));
            return;
        }

        // 参数校验：不合法直接拦下，避免用非法参数空转协程
        if (!ValidateBaseParams(out string paramErr))
        {
            SetDesignMessage("⚠ " + paramErr, new Color(0.95f, 0.30f, 0.30f));
            Debug.LogWarning("[UIManager] 生成已拦截（参数校验失败）：" + paramErr);
            return;
        }

        // 方案4：生成前可行性预检——避免在不可达设置上白白空转（默认 5 秒/关）
        try
        {
            var cfgCheck = BuildConfigFromUI();
            if (!LevelGenerator.CheckFeasibility(cfgCheck, out string reason))
            {
                SetDesignMessage("⚠ " + reason, new Color(0.95f, 0.75f, 0.20f));
                Debug.LogWarning("[UIManager] 生成已拦截（方案4 可行性预检）：" + reason);
                return;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[UIManager] 可行性预检异常，跳过预检继续生成：" + e.Message);
        }

        _genCancelled = false;
        _isGenerating = true;
        SetGenerateButtonState(true);      // 按钮变红显示「取消生成」并保持可点击
        SetDesignMessage("", Color.white);
        StartCoroutine(CoGenerateLevels());
    }

    /// <summary>同步/刷新进度条显示：0~1 fillAmount + 完成/总数 + 百分比 + 超时数量。</summary>
    private void UpdateGenProgress(float p, int done, int total, int timeoutCount)
    {
        if (_genProgressFill != null)
        {
            _genProgressFill.fillAmount = Mathf.Clamp01(p);
        }
        if (_genProgressLabel != null)
        {
            if (timeoutCount > 0)
                _genProgressLabel.text = $"{done} / {total}    {Mathf.RoundToInt(p * 100f)}%    超时 {timeoutCount}";
            else
                _genProgressLabel.text = $"{done} / {total}    {Mathf.RoundToInt(p * 100f)}%";
        }
    }

    /// <summary>
    /// 逐关生成关卡：每生成一关 yield return null 让 Unity 渲染一帧，玩家能看到进度条持续走动。
    /// —— 结构说明（CS1626 编译限制）：C# 禁止 yield return 出现在「带 catch 的 try 块」里；
    ///    但 yield 可以出现在「只带 finally、无 catch 的 try 块」里。因此采用「外层 try/finally + 内部同步段小 try/catch（不含 yield）」方案。
    /// </summary>
    private System.Collections.IEnumerator CoGenerateLevels()
    {
        if (!int.TryParse(genCountInput?.text ?? "", out int N)) N = 5;
        N = Mathf.Max(1, Mathf.Min(50, N));
        var cfg = BuildConfigFromUI();

        List<LevelInstance> generated = new List<LevelInstance>(N);
        int timeoutCount = 0;
        Exception fatalErr = null;
        bool cancelled = false;

        // 外层只有 finally 的 try：容纳所有 yield（合法）；finally 必执行：恢复按钮 + 关进度条
        try
        {
            // —— 同步段 1：初始 UI 状态初始化（不含 yield）
            //    注意：生成期间按钮必须保持可点击，玩家点击它即「取消生成」，故不在此禁用按钮。
            try
            {
                if (_genProgressBar != null) _genProgressBar.SetActive(true);
                UpdateGenProgress(0f, 0, N, 0);
                SetDesignMessage($"正在生成 {N} 个关卡（单关卡超时 {LevelGenerator.TIMEOUT_MS_PER_LEVEL}ms）… 再次点击按钮可取消。",
                    new Color(0.45f, 0.8f, 1f));
                // —— 关键：生成前清空旧关卡，保证关卡列表严格等于本次设定数量 N（不累计旧批次）——
                if (LevelRecordManager.Instance != null)
                {
                    int oldCount = LevelRecordManager.Instance.GetLevels().Count;
                    LevelRecordManager.Instance.ClearAll();
                    Debug.Log($"[LevelDesigner] 生成前清空 {oldCount} 个旧关卡 → 本次严格生成 {N} 个新关卡。");
                }
            }
            catch (Exception e)
            {
                fatalErr = e;
                Debug.LogError("[CoGenerateLevels] 初始化 UI 异常：" + e);
                SetDesignMessage("初始化失败：" + e.Message, new Color(0.95f, 0.30f, 0.30f));
            }
            // （yield 放在 try/finally 内，合法）立即渲染一帧显示初始 UI，避免第一关的同步生成把 UI 卡住
            yield return null;
            if (fatalErr != null) yield break;

            // —— 逐关生成循环（每轮开始检查取消标志：取消后保留已生成的关卡）
            for (int i = 0; i < N; i++)
            {
                if (_genCancelled)
                {
                    cancelled = true;
                    Debug.Log($"[CoGenerateLevels] 已取消：完成 {generated.Count}/{N} 关后停止。");
                    break;
                }

                LevelInstance lv = null;
                bool timedOut = false;

                // 同步段 2：单关生成 + 存档（不含 yield）
                try
                {
                    lv = LevelGenerator.GenerateSingle(cfg, out timedOut);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[CoGenerateLevels] 第 {i + 1}/{N} 关 GenerateSingle 异常：{e}");
                    lv = null;
                }
                // 极端兜底：GenerateSingle 理论 100% 返回非空，若为 null 按超时计数但不中断循环
                if (lv == null)
                {
                    timedOut = true;
                    lv = new LevelInstance
                    {
                        levelId = $"FALLBACK_{Guid.NewGuid():N}",
                        createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        config = cfg ?? new LevelConfig(),
                        metrics = new DifficultyAnalyzer.DifficultyMetrics(),
                        overallDD = 0.5f,
                        gridSnapshot = new List<int>(),
                        generationTimedOut = true,
                        remark = "未知生成失败，使用空关卡占位（建议重试）"
                    };
                }
                if (timedOut) timeoutCount++;
                generated.Add(lv);
                // 逐关存档（Unity 崩溃/玩家中途退出也不丢已生成关卡）
                try
                {
                    if (LevelRecordManager.Instance != null)
                    {
                        LevelRecordManager.Instance.UpsertLevel(lv);
                        LevelRecordManager.Instance.SaveAll();
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[CoGenerateLevels] 第 {i + 1}/{N} 关存档异常：{e}");
                }

                // —— 同步段 3：进度条 UI 更新（不含 yield，不抛）
                float p = (float)(i + 1) / N;
                UpdateGenProgress(p, i + 1, N, timeoutCount);

                // （yield 放在外层只有 finally 的 try 内，合法）让 Unity 渲染一帧：进度条一格一格向右走
                yield return null;
            }

            // —— 完成态：进度条先到 100% 再停 0.8s 让玩家看清。
            //    yield 只能出现在「只含 finally、不含 catch」的外层 try 内，故此处单独成段。
            if (!cancelled)
            {
                UpdateGenProgress(1f, N, N, timeoutCount);
                yield return new WaitForSeconds(0.8f);
            }

            // —— 同步段 4：结果汇总 + 跳列表页（本段不含 yield，可以安全地包在 try/catch 中）
            try
            {
                if (cancelled)
                {
                    string cancelMsg = generated.Count > 0
                        ? $"已取消生成：保留前 {generated.Count}/{N} 个关卡（可在关卡列表中查看）。"
                        : "已取消生成：本次尚未产出关卡。";
                    Debug.Log($"[LevelDesigner] {cancelMsg}");
                    SetDesignMessage(cancelMsg, new Color(0.95f, 0.75f, 0.20f));
                    if (generated.Count > 0) OpenLevelSelector();
                }
                else
                {
                    string resultMsg;
                    if (generated.Count == 0)
                    {
                        resultMsg = "生成失败：没有产出任何关卡。建议：减小尺寸、减少类型数、或把 8 指标都设为「普通」区间后重试。";
                        SetDesignMessage(resultMsg, new Color(0.95f, 0.30f, 0.30f));
                    }
                    else if (timeoutCount == 0)
                    {
                        resultMsg = $"生成 {generated.Count}/{N} 个关卡成功。";
                        Debug.Log($"[LevelDesigner] {resultMsg}");
                        SetDesignMessage($"{resultMsg} 点击关卡列表卡片「开始体验」进入。", new Color(0.35f, 0.85f, 0.45f));
                        OpenLevelSelector();
                    }
                    else
                    {
                        resultMsg = $"生成 {generated.Count}/{N} 个关卡完成，其中 {timeoutCount} 个关卡超时（已返回最接近目标的关卡，指标可能未完全命中目标等级）。";
                        Debug.LogWarning($"[LevelDesigner] {resultMsg}");
                        SetDesignMessage(resultMsg, new Color(0.95f, 0.75f, 0.20f));
                        OpenLevelSelector();
                    }
                }
            }
            catch (Exception e)
            {
                fatalErr = e;
                Debug.LogError("[CoGenerateLevels] 结果汇总或跳转关卡列表异常：" + e);
                SetDesignMessage("生成失败：" + e.Message, new Color(0.95f, 0.30f, 0.30f));
            }
        }
        finally
        {
            _isGenerating = false;
            _genCancelled = false;
            SetGenerateButtonState(false);   // 恢复橙色「生成关卡」
            if (_genProgressBar != null)
            {
                // 失败时停留 1.2s 让玩家读完错误后再关进度条；其余情况立即关
                if (fatalErr != null)
                    StartCoroutine(CloseProgressBarDelayed(1.2f));
                else
                    _genProgressBar.SetActive(false);
            }
        }
    }

    /// <summary>生成失败时延迟关闭进度条（让玩家读完错误）。</summary>
    private System.Collections.IEnumerator CloseProgressBarDelayed(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (_genProgressBar != null) _genProgressBar.SetActive(false);
    }

    /// <summary>重建关卡列表：根据 LevelRecordManager 中所有关卡渲染卡片。</summary>
    private void RebuildLevelList()
    {
        if (levelListContainer == null) return;
        foreach (var obj in _levelCardObjects) Destroy(obj);
        _levelCardObjects.Clear();
        RectTransform contentRT = levelListContainer.GetComponent<RectTransform>();
        if (contentRT == null) return;
        var list = LevelRecordManager.Instance == null ? new List<LevelInstance>() : LevelRecordManager.Instance.GetLevels();

        // 每个卡片 高 110；间距 10
        float cardW = 800f, cardH = 110f, gap = 10f;
        float yAcc = 10f;

        Font font = null;
        if (scoreText != null) font = scoreText.font;

        if (LevelRecordManager.Instance == null)
            Debug.LogWarning("[RebuildLevelList] LevelRecordManager.Instance == null：列表将为空，请检查 GameInitializer.Start() 是否初始化了 LevelRecordManager。");

        for (int i = 0; i < list.Count; i++)
        {
            var lv = list[i];
            GameObject card = new GameObject($"LevelCard_{lv.id}");
            card.transform.SetParent(levelListContainer.transform, false);
            RectTransform crt = card.AddComponent<RectTransform>();
            crt.anchorMin = new Vector2(0.5f, 1);
            crt.anchorMax = new Vector2(0.5f, 1);
            crt.pivot = new Vector2(0.5f, 1);
            crt.sizeDelta = new Vector2(cardW, cardH);
            crt.anchoredPosition = new Vector2(0, -yAcc);
            Image cardBg = card.AddComponent<Image>();
            cardBg.color = new Color(0.18f, 0.18f, 0.28f);
            _levelCardObjects.Add(card);
            yAcc += cardH + gap;

            // 文字：关卡编号 / 尺寸 / 类型数 / 综合 DD / 最佳成绩 / 生成状态（超时/未完全达标标注）
            var g = DifficultyAnalyzer.NormalizedValueToGrade(lv.overallDD);
            string head = $"#{i + 1}  关卡 {lv.id}" + (lv.generationTimedOut ? "  ⚠未完全达标" : "");
            string info = $"尺寸 {lv.config.rows}×{lv.config.cols}  类型数 {lv.config.typeCount}";
            string ddStr = $"综合 DD: {lv.overallDD:F3} ({DifficultyGradeUtil.ToName(g)})";
            string best = lv.bestTime > 0 ? $"最佳用时: {lv.bestTime:F2} 秒" : "暂无通关记录";
            string recordsCount = (lv.clearRecords?.Count ?? 0) > 0 ? $"通关次数: {lv.clearRecords.Count}" : "通关次数: 0";
            // remark：正常关卡为空串不显示；超时/未完全达标/失败兜底的关卡有中文提示，黄色标注给玩家确认
            string remarkText = !string.IsNullOrEmpty(lv.remark) ? ("状态: " + lv.remark) : string.Empty;

            // 文字右侧统一预留 380 像素（右侧按钮区 180 宽 + 左右空距 100），保证不覆盖按钮
            float textRightInset = 380f;

            Text headTxt = MakeUIText(card.transform, "Head", head,
                new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(15, -10), new Vector2(-textRightInset, -10),
                22, TextAnchor.UpperLeft, font);
            if (lv.generationTimedOut) headTxt.color = new Color(1f, 0.78f, 0.25f);

            Text infoTxt = MakeUIText(card.transform, "Info", $"{info}\n{ddStr}\n{best}   {recordsCount}" + (string.IsNullOrEmpty(remarkText) ? "" : $"\n{remarkText}"),
                new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(15, -40), new Vector2(-textRightInset, -15),
                15, TextAnchor.UpperLeft, font);
            infoTxt.horizontalOverflow = HorizontalWrapMode.Wrap;
            infoTxt.verticalOverflow = VerticalWrapMode.Truncate;
            if (!string.IsNullOrEmpty(remarkText)) infoTxt.color = new Color(1f, 0.82f, 0.4f);

            // 右侧按钮区域：上=开始体验橙 / 下=查看记录蓝，右对齐卡片右侧
            // 坐标说明（父 = 800×110 的卡片矩形，UGUI y 向上，父底 y=0，父顶 y=110）：
            //   锚点 anchor = (1, 0) → 卡片右下角 (800, 0)
            //   按钮 pivot = (1, 0.5) → 按钮自身右边缘中心
            //   anchoredOffset.x = -20 → 按钮右边缘 = 800 - 20 = 780（卡片右 20px 留白，绝对不超出被 RectMask 裁）
            //   anchoredOffset.y = 81 → 按钮中心父 y = 0 + 81 = 81，y 范围 [81-21, 81+21] = [60, 102]（距顶 8px）
            //   anchoredOffset.y = 29 → 按钮中心父 y = 0 + 29 = 29，y 范围 [ 8, 50]（距底 8px）
            //   两按钮 y 不重叠：[60,102] 与 [8,50] 之间有 [50,60] 10 像素白空
            Vector2 btnSize = new Vector2(150, 42);
            float rightMargin = 20f;
            Vector2 btnAnchor = new Vector2(1, 0);
            Vector2 btnPivot = new Vector2(1, 0.5f);
            Button playBtn = MakeUIButton(card.transform, "PlayBtn", "开始体验",
                btnAnchor, btnAnchor,
                new Vector2(-rightMargin, 81f),
                btnSize, font,
                pivot: btnPivot);
            // 橙色：强烈提示"挑战关卡"的主要入口
            playBtn.GetComponent<Image>().color = new Color(0.92f, 0.55f, 0.15f);

            Button recBtn = MakeUIButton(card.transform, "RecBtn", "查看记录",
                btnAnchor, btnAnchor,
                new Vector2(-rightMargin, 29f),
                btnSize, font,
                pivot: btnPivot);
            recBtn.GetComponent<Image>().color = new Color(0.3f, 0.55f, 1f);

            // 第三个按钮「指标报告」：放在两按钮左侧一列（卡片文本区右边界 x=420，本按钮左边界 460，不重叠）
            Button metricBtn = MakeUIButton(card.transform, "MetricBtn", "指标报告",
                btnAnchor, btnAnchor,
                new Vector2(-190f, 55f),
                btnSize, font,
                pivot: btnPivot);
            metricBtn.GetComponent<Image>().color = new Color(0.30f, 0.68f, 0.62f);

            string lid = lv.id; // 闭包
            playBtn.onClick.AddListener(() => OnPlayLevel(lid));
            recBtn.onClick.AddListener(() => ShowRecordPanel(lid));
            metricBtn.onClick.AddListener(() => ShowMetricReport(lid));
        }
        contentRT.sizeDelta = new Vector2(contentRT.sizeDelta.x, yAcc + 20f);
    }

    /// <summary>玩家点击某关卡「开始体验」→ 加载关卡并进入游戏 HUD。</summary>
    private void OnPlayLevel(string levelId)
    {
        EnsureLevelClearedSubscribed();   // 防御：确保通关事件订阅存在（防初始化时序问题）
        Debug.Log($"[OnPlayLevel] 收到跳转请求：levelId={levelId}");

        if (LevelRecordManager.Instance == null)
        {
            Debug.LogError("[OnPlayLevel] LevelRecordManager.Instance == null：GameInitializer 可能没挂载/初始化记录管理器。请检查 GameInitializer.Start()。");
            return;
        }
        var lv = LevelRecordManager.Instance.FindLevel(levelId);
        if (lv == null)
        {
            Debug.LogError($"[OnPlayLevel] 找不到关卡：levelId={levelId}。请刷新列表或重新生成。");
            return;
        }

        try
        {
            // —— 面板显隐：显式关闭所有设计/列表/记录面板，强制显示 gameHUD
            if (titlePanel != null) titlePanel.SetActive(false);
            if (levelDesignPanel != null) levelDesignPanel.SetActive(false);
            if (levelSelectorPanel != null) levelSelectorPanel.SetActive(false);
            if (recordPanel != null) recordPanel.SetActive(false);
            if (metricReportPanel != null) metricReportPanel.SetActive(false);
            if (gameHud != null) gameHud.SetActive(true);

            if (GameController.Instance == null)
            {
                Debug.LogError("[OnPlayLevel] GameController.Instance == null：场景中没有 GameController，跳转终止。请检查 GameInitializer.Start()。");
                return;
            }

            Debug.Log($"[OnPlayLevel] 调用 GameController.StartNewGame({lv.id})，尺寸 {lv.config.rows}x{lv.config.cols}，类型 {lv.config.typeCount}，DD {lv.overallDD:F3}");
            // 标记来源：从关卡列表页「开始体验」进入 → HUD 右上按钮应是「返回关卡列表」（回到生成关卡列表页而不是标题页）
            _lastFromLevelList = true;
            SyncHudBackButtonText();
            GameController.Instance.StartNewGame(lv);
            Debug.Log("[OnPlayLevel] 启动完成，已进入游戏 HUD（分数=0，时间=0，剩余对数已同步）。");
        }
        catch (Exception e)
        {
            Debug.LogError($"[OnPlayLevel] 进入关卡异常：\n{e}");
        }
    }

    // ================================================================
    // 通关记录写入 + 记录面板渲染 + 删除
    // ================================================================

    /// <summary>GameController 通关事件回调：写入通关时间到记录管理器。</summary>
    private void OnGameLevelCleared(string levelId, float clearTime)
    {
        // —— 无条件打日志（便于排查成绩不存的问题），玩家可以在 Console 搜索 [Record] 看到所有通关事件的触发
        string safeId = levelId == null ? "(null)" : levelId.Length == 0 ? "(empty)" : levelId;
        Debug.Log($"[Record] 收到通关事件：levelId='{safeId}'  clearTime={clearTime:F2}s");

        if (string.IsNullOrEmpty(levelId))
        {
            // 标题页普通随机局：没有关联的关卡实例，本身就不该存
            Debug.Log("[Record] levelId 为空（常规随机通关，不关联自定义关卡实例）→ 跳过写入。");
            return;
        }
        if (LevelRecordManager.Instance == null)
        {
            Debug.LogWarning("[Record] LevelRecordManager.Instance 为 null → 无法保存。");
            return;
        }
        LevelInstance lv = LevelRecordManager.Instance.FindLevel(levelId);
        if (lv == null)
        {
            Debug.LogWarning($"[Record] 未在记录管理器中找到关卡 id='{levelId}' → AddClearRecord 不会写入任何数据，已补显式日志以确认链路。");
            return;
        }
        LevelRecordManager.Instance.AddClearRecord(levelId, clearTime);
        LevelRecordManager.Instance.SaveAll();
        Debug.Log($"[Record] ✅ 保存成功：关卡 id='{levelId}' 通关用时 {clearTime:F2}s。" +
                  $" 新 bestTime={lv.bestTime:F2}s，历史共 {lv.clearRecords.Count} 条记录。");
    }

    /// <summary>重建记录列表：渲染 _currentRecordLevel 的所有 clearRecords（含表头排序/列对齐/空态提示）。</summary>
    private void RebuildRecordList()
    {
        if (recordsListContainer == null) return;
        foreach (var obj in _recordEntryObjects) Destroy(obj);
        _recordEntryObjects.Clear();
        _recordRowObjects.Clear();
        _recordOrigIndexOrder.Clear();
        RectTransform contentRT = recordsListContainer.GetComponent<RectTransform>();
        if (contentRT == null) return;

        if (currentRecordLevelLabel != null)
        {
            if (_currentRecordLevel == null)
                currentRecordLevelLabel.text = "当前关卡：(暂无)";
            else
            {
                string id = _currentRecordLevel.id;
                if (id.Length > 18) id = id.Substring(0, 15) + "...";
                // —— 防御：旧存档反序列化后 config 可能为 null，直接访问会抛 NRE 中断整个重建 → 记录列表空白
                string sizeStr = (_currentRecordLevel.config != null)
                    ? $"{_currentRecordLevel.config.rows}×{_currentRecordLevel.config.cols}"
                    : "未知";
                currentRecordLevelLabel.text =
                    $"当前关卡：{id}  |  尺寸 {sizeStr}  |  DD {_currentRecordLevel.overallDD:F3}" +
                    $"  |  通关次数 {(_currentRecordLevel.clearRecords?.Count ?? 0)}";
            }
        }

        try
        {
        Font font = scoreText?.font;
        float yAcc = 10f;               // 距顶累加（向下为正）
        const float HEADER_H = 40f;     // 表头行高
        const float ENTRY_H = 44f;      // 条目行高
        const float GAP = 8f;           // 行距
        const float INNER_PAD_L = 20f;  // 最左内边距
        const float INNER_PAD_R = 20f;  // 最右内边距
        // 4 列固定宽度（绝不重叠）：序号 / 通关时长 / 通关日期 / 删除
        const float COL_W_IDX = 110f;
        const float COL_W_DUR = 180f;
        const float COL_W_DATE = 260f;
        const float COL_W_DEL = 120f;
        const float COL_GAP = 10f;      // 列与列之间的空白
        float x1c = INNER_PAD_L + COL_W_IDX / 2f;
        float x2c = x1c + COL_W_IDX / 2f + COL_GAP + COL_W_DUR / 2f;
        float x3c = x2c + COL_W_DUR / 2f + COL_GAP + COL_W_DATE / 2f;
        // 第 4 列（删除）右对齐距右 INNER_PAD_R
        float x4c = -1; // 将在计算完 contentRT 宽度后赋值

        int n = _currentRecordLevel?.clearRecords?.Count ?? 0;

        // —————— 计算内容宽度用于"删除"列定位 ——————
        float contentW = contentRT.rect.width > 0 ? contentRT.rect.width : 1200f;
        x4c = (contentW - INNER_PAD_R) - COL_W_DEL / 2f;

        // ================================================================
        // 1) 表头行：4 列，每列是可点击按钮（切换排序）+ 方向箭头
        // ================================================================
        GameObject headerObj = new GameObject("HeaderRow");
        headerObj.transform.SetParent(recordsListContainer.transform, false);
        RectTransform hrt = headerObj.AddComponent<RectTransform>();
        hrt.anchorMin = new Vector2(0, 1); hrt.anchorMax = new Vector2(1, 1);
        hrt.pivot = new Vector2(0.5f, 1);
        hrt.sizeDelta = new Vector2(0, HEADER_H);
        hrt.anchoredPosition = new Vector2(0, -yAcc);
        Image hImg = headerObj.AddComponent<Image>();
        hImg.color = new Color(0.25f, 0.25f, 0.4f);
        hImg.raycastTarget = false; // 背景不拦截点击，让子按钮自己接
        _recordEntryObjects.Add(headerObj);
        yAcc += HEADER_H + GAP;

        // 表头辅助：生成 1 个列的"可点按钮 + 文字 + ↓/↑ 箭头"
        void MakeHeaderCol(string name, string displayName, RecSortField field, float xCenter, float colW)
        {
            bool isCur = _recSortField == field;
            string arrow = isCur ? (_recSortAsc ? "  ↑" : "  ↓") : "   ";
            GameObject go = new GameObject(name);
            go.transform.SetParent(headerObj.transform, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(colW, HEADER_H - 6);
            rt.anchoredPosition = new Vector2(xCenter, -(HEADER_H) / 2f);
            Image bImg = go.AddComponent<Image>();
            bImg.color = isCur ? new Color(0.42f, 0.42f, 0.68f) : new Color(0.32f, 0.32f, 0.5f);
            bImg.raycastTarget = true;
            Button b = go.AddComponent<Button>();
            ColorBlock cb = b.colors;
            cb.normalColor = Color.white; cb.highlightedColor = new Color(0.92f, 0.92f, 1f);
            cb.pressedColor = new Color(0.8f, 0.8f, 0.9f);
            b.colors = cb;
            b.targetGraphic = bImg;
            b.onClick.AddListener(() =>
            {
                if (_recSortField == field) _recSortAsc = !_recSortAsc;   // 同列再次点击 → 翻转
                else { _recSortField = field; _recSortAsc = (field == RecSortField.Duration); /* Duration 默认升（快→慢）；Index 默认升 1..N；Date 默认降 新→旧 */ }
                if (field == RecSortField.Date && _recSortField != field) _recSortAsc = false;
                RebuildRecordList();
            });
            Text t = MakeUITextFixed(go.transform, "Lbl", displayName + arrow,
                new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(0, 0), /* pivot 中心 */ new Vector2(0.5f, 0.5f),
                new Vector2(colW - 6, HEADER_H - 10),
                16, TextAnchor.MiddleCenter, font);
            _recordEntryObjects.Add(go);
            _ = t;
        }

        MakeHeaderCol("H_Index", "通关序号", RecSortField.Index, x1c, COL_W_IDX);
        MakeHeaderCol("H_Dur", "通关时长", RecSortField.Duration, x2c, COL_W_DUR);
        MakeHeaderCol("H_Date", "通关日期", RecSortField.Date, x3c, COL_W_DATE);
        // 删除列：表头是灰色说明文字"操作"，不可排序
        {
            GameObject go = new GameObject("H_Del");
            go.transform.SetParent(headerObj.transform, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(COL_W_DEL, HEADER_H - 6);
            rt.anchoredPosition = new Vector2(x4c, -(HEADER_H) / 2f);
            Image bg = go.AddComponent<Image>(); bg.color = new Color(0.22f, 0.22f, 0.38f); bg.raycastTarget = false;
            MakeUITextFixed(go.transform, "Lbl", "操作",
                new Vector2(0, 1), new Vector2(0, 1),
                Vector2.zero, new Vector2(0.5f, 0.5f), new Vector2(COL_W_DEL - 6, HEADER_H - 10),
                16, TextAnchor.MiddleCenter, font);
            _recordEntryObjects.Add(go);
        }

        _selectedRecordIndex = -1;

        // ================================================================
        // 2) 构建并排序记录
        // ================================================================
        List<RecEntry> entries = new List<RecEntry>();
        for (int i = 0; i < n; i++)
        {
            float dur = _currentRecordLevel.clearRecords[i];
            string tsStr = i < (_currentRecordLevel.recordTimestamps?.Count ?? 0) ? _currentRecordLevel.recordTimestamps[i] : "-";
            DateTime tsDt;
            if (!DateTime.TryParse(tsStr, out tsDt)) tsDt = DateTime.MinValue;
            entries.Add(new RecEntry { origIndex = i, duration = dur, tsStr = tsStr, tsDt = tsDt });
        }
        switch (_recSortField)
        {
            case RecSortField.Index:
                entries.Sort((a, b) => _recSortAsc ? a.origIndex.CompareTo(b.origIndex) : b.origIndex.CompareTo(a.origIndex));
                break;
            case RecSortField.Duration:
                entries.Sort((a, b) => _recSortAsc ? a.duration.CompareTo(b.duration) : b.duration.CompareTo(a.duration));
                break;
            case RecSortField.Date:
                entries.Sort((a, b) => _recSortAsc ? a.tsDt.CompareTo(b.tsDt) : b.tsDt.CompareTo(a.tsDt));
                break;
        }
        // 保存排序后的原始索引映射（选中删除时能找到真正该删的那条 clearRecords 索引）
        foreach (var e in entries) _recordOrigIndexOrder.Add(e.origIndex);

        // ================================================================
        // 3) 渲染条目或空态提示
        // ================================================================
        if (entries.Count == 0)
        {
            // 空态：在 Content 中央显示提示（绝对居中）
            GameObject empty = new GameObject("EmptyHint");
            empty.transform.SetParent(recordsListContainer.transform, false);
            RectTransform ert = empty.AddComponent<RectTransform>();
            ert.anchorMin = new Vector2(0.5f, 0.5f); ert.anchorMax = new Vector2(0.5f, 0.5f);
            ert.pivot = new Vector2(0.5f, 0.5f);
            ert.sizeDelta = new Vector2(720, 140);
            ert.anchoredPosition = new Vector2(0, -yAcc - 60);
            Image ebg = empty.AddComponent<Image>(); ebg.color = new Color(0.12f, 0.12f, 0.22f, 0.6f); ebg.raycastTarget = false;
            MakeUITextFixed(empty.transform, "T1", "📭 尚无通关记录",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0, 40), new Vector2(0.5f, 0.5f), new Vector2(700, 34),
                22, TextAnchor.MiddleCenter, font, new Color(0.95f, 0.78f, 0.35f));
            MakeUITextFixed(empty.transform, "T2", "从关卡列表点「开始体验」挑战一次，通关后回来就能在这看到：通关序号 / 通关时长 / 通关日期。\n点击表头「通关序号 / 通关时长 / 通关日期」可切换排序顺序。",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0, -14), new Vector2(0.5f, 0.5f), new Vector2(680, 80),
                16, TextAnchor.MiddleCenter, font, new Color(0.8f, 0.85f, 0.95f));
            _recordEntryObjects.Add(empty);
            yAcc += 200f;
        }
        else
        {
            for (int i = 0; i < entries.Count; i++)
            {
                RecEntry e = entries[i];
                int listPos = i;   // 排序后的当前显示位置（用于选中高亮）
                int origIdx = e.origIndex;  // 真实第几次通关（序号显示#N 和删除时的列表索引）

                GameObject entry = new GameObject($"Record_{origIdx}_at_{i}");
                entry.transform.SetParent(recordsListContainer.transform, false);
                RectTransform ert = entry.AddComponent<RectTransform>();
                ert.anchorMin = new Vector2(0, 1); ert.anchorMax = new Vector2(1, 1);
                ert.pivot = new Vector2(0.5f, 1);
                ert.sizeDelta = new Vector2(0, ENTRY_H);
                ert.anchoredPosition = new Vector2(0, -yAcc);
                Image eImg = entry.AddComponent<Image>();
                eImg.color = new Color(0.15f, 0.15f, 0.23f);
                eImg.raycastTarget = true;
                Button eBtn = entry.AddComponent<Button>();
                ColorBlock cb = eBtn.colors;
                cb.normalColor = Color.white; cb.highlightedColor = new Color(0.96f, 0.96f, 1f);
                cb.pressedColor = new Color(0.85f, 0.85f, 0.95f);
                eBtn.colors = cb;
                eBtn.targetGraphic = eImg;
                _recordEntryObjects.Add(entry);
                _recordRowObjects.Add(entry);
                yAcc += ENTRY_H + GAP;

                // 选中行：点整行更新 _selectedRecordIndex（底部"删除选中记录"按钮仍有效）
                int captured = i;
                eBtn.onClick.AddListener(() =>
                {
                    _selectedRecordIndex = captured;
                    RefreshRecordEntrySelection();
                });

                // —— 4 列（顺序与表头严格对齐，固定 xCenter 绝不重叠）
                MakeUITextFixed(entry.transform, "I1", $"#{origIdx + 1}",
                    new Vector2(0, 1), new Vector2(0, 1),
                    new Vector2(x1c, -(ENTRY_H) / 2f), new Vector2(0.5f, 0.5f), new Vector2(COL_W_IDX - 4, ENTRY_H - 10),
                    16, TextAnchor.MiddleCenter, font, new Color(0.95f, 0.95f, 0.55f));

                int m = Mathf.FloorToInt(e.duration / 60f);
                int s = Mathf.FloorToInt(e.duration % 60f);
                int cs = Mathf.FloorToInt((e.duration - Mathf.Floor(e.duration)) * 100f);
                string durStr = $"{m:00}:{s:00}.{cs:00}";
                Color durColor = new Color(0.55f, 1f, 0.7f);
                // 标最佳用时（duration==bestTime）为金绿色
                if (Mathf.Abs(e.duration - (_currentRecordLevel.bestTime < 0 ? float.MaxValue : _currentRecordLevel.bestTime)) < 0.001f)
                    durColor = new Color(0.95f, 0.85f, 0.35f);
                MakeUITextFixed(entry.transform, "I2", durStr,
                    new Vector2(0, 1), new Vector2(0, 1),
                    new Vector2(x2c, -(ENTRY_H) / 2f), new Vector2(0.5f, 0.5f), new Vector2(COL_W_DUR - 4, ENTRY_H - 10),
                    16, TextAnchor.MiddleCenter, font, durColor);

                MakeUITextFixed(entry.transform, "I3", e.tsStr,
                    new Vector2(0, 1), new Vector2(0, 1),
                    new Vector2(x3c, -(ENTRY_H) / 2f), new Vector2(0.5f, 0.5f), new Vector2(COL_W_DATE - 4, ENTRY_H - 10),
                    16, TextAnchor.MiddleCenter, font, new Color(0.85f, 0.95f, 1f));

                // —— 第 4 列：行内删除按钮（红色，比底部"删除选中"更直观）
                Button inlineDel = MakeUIButton(entry.transform, "InlineDel", "删除",
                    new Vector2(0, 1), new Vector2(0, 1),
                    new Vector2(x4c, -(ENTRY_H) / 2f),
                    new Vector2(COL_W_DEL - 6, ENTRY_H - 14),
                    font, new Vector2(0.5f, 0.5f));
                Image idi = inlineDel.GetComponent<Image>();
                if (idi != null) idi.color = new Color(0.8f, 0.3f, 0.3f);
                _recordEntryObjects.Add(inlineDel.gameObject);
                int origIdxForDel = origIdx;
                inlineDel.onClick.AddListener(() => DeleteRecordByOriginalIndex(origIdxForDel));
            }
        }

        // 选中高亮刷新
        RefreshRecordEntrySelection();
        // 设置 Content 总高度（让 ScrollRect 可滚动）
        contentRT.sizeDelta = new Vector2(contentRT.sizeDelta.x, yAcc + 20f);
        }
        catch (Exception e)
        {
            // 任何异常都不让面板白屏：打日志并回退显示一个占位提示，玩家能确认"是数据没到 / 还是 UI 出错"
            Debug.LogError("[Record] RebuildRecordList 重建记录列表异常（记录列表可能显示不完整）：" + e);
            try
            {
                if (_currentRecordLevel == null || _currentRecordLevel.clearRecords == null || _currentRecordLevel.clearRecords.Count == 0)
                {
                    GameObject empty = new GameObject("EmptyHint");
                    empty.transform.SetParent(recordsListContainer.transform, false);
                    RectTransform ert = empty.AddComponent<RectTransform>();
                    ert.anchorMin = new Vector2(0.5f, 0.5f); ert.anchorMax = new Vector2(0.5f, 0.5f);
                    ert.pivot = new Vector2(0.5f, 0.5f);
                    ert.sizeDelta = new Vector2(720, 100);
                    Image ebg = empty.AddComponent<Image>();
                    ebg.color = new Color(0.12f, 0.12f, 0.22f, 0.6f);
                    ebg.raycastTarget = false;
                    MakeUITextFixed(empty.transform, "T1", "⚠ 记录加载失败，请尝试重新生成关卡",
                        new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                        Vector2.zero, new Vector2(0.5f, 0.5f), new Vector2(700, 34),
                        18, TextAnchor.MiddleCenter, scoreText?.font, new Color(0.95f, 0.45f, 0.35f));
                    _recordEntryObjects.Add(empty);
                }
            }
            catch { }
        }
    }

    /// <summary>按原始 clearRecords 的索引删除单条记录（删除后自动刷新排序和列表）。</summary>
    private void DeleteRecordByOriginalIndex(int origIndex)
    {
        if (_currentRecordLevel == null || LevelRecordManager.Instance == null) return;
        if (origIndex < 0 || _currentRecordLevel.clearRecords == null ||
            origIndex >= _currentRecordLevel.clearRecords.Count) return;
        _currentRecordLevel.clearRecords.RemoveAt(origIndex);
        if (_currentRecordLevel.recordTimestamps != null && origIndex < _currentRecordLevel.recordTimestamps.Count)
            _currentRecordLevel.recordTimestamps.RemoveAt(origIndex);
        float best = -1f;
        if (_currentRecordLevel.clearRecords != null)
            foreach (var t in _currentRecordLevel.clearRecords)
                if (best < 0 || t < best) best = t;
        _currentRecordLevel.bestTime = best;
        LevelRecordManager.Instance.SaveAll();
        _selectedRecordIndex = -1;
        Debug.Log($"[Record] 已删除单条记录：关卡 id='{_currentRecordLevel.id}'，原始第 {origIndex + 1} 次通关。");
        RebuildRecordList();
    }

    /// <summary>
    /// 记录 UI 专用：基于左上角 (0,1) 锚点 + 明确 position/size/pivot 创建文字；
    /// 彻底规避"offsetMax.x 正数导致父矩形外溢 + 列重叠"的旧 bug。
    /// </summary>
    private static Text MakeUITextFixed(Transform parent, string name, string content,
        Vector2 anchorMin, Vector2 anchorMax,
        Vector2 anchoredPos, Vector2 pivot, Vector2 size,
        int fontSize, TextAnchor anchor, Font font, Color? overrideColor = null)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        Text t = obj.AddComponent<Text>();
        t.text = content ?? string.Empty;
        t.font = font;
        t.fontSize = fontSize;
        t.alignment = anchor;
        t.color = overrideColor ?? Color.white;
        // HorizontalWrapMode 只有 Wrap/Overflow 两值（Truncate 仅存在于 VerticalWrapMode）。
        // 水平方向用 Overflow（文字锚点 MiddleCenter + 明确列宽区域，本身不会跑出矩形太多），
        // 垂直方向用 Truncate 防止行内超高的文字溢出到下一行。
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        return t;
    }

    private void RefreshRecordEntrySelection()
    {
        // 只遍历"条目行根"列表（排序后顺序），不再关心 _recordEntryObjects 里混入的表头/子按钮/空态对象
        for (int i = 0; i < _recordRowObjects.Count; i++)
        {
            Image img = _recordRowObjects[i].GetComponent<Image>();
            if (img == null) continue;
            img.color = (i == _selectedRecordIndex)
                ? new Color(0.35f, 0.35f, 0.7f)       // 选中：蓝紫色高亮
                : new Color(0.15f, 0.15f, 0.23f);      // 未选中：默认暗色
        }
    }

    /// <summary>删除当前关卡的选中记录（底部红色按钮）。先通过排序映射找到 origIndex，再统一走 DeleteRecordByOriginalIndex。</summary>
    private void OnDeleteSelectedRecord()
    {
        if (_currentRecordLevel == null || LevelRecordManager.Instance == null) return;
        if (_selectedRecordIndex < 0 || _selectedRecordIndex >= _recordOrigIndexOrder.Count) return;
        int origIdx = _recordOrigIndexOrder[_selectedRecordIndex];
        Debug.Log($"[Record] 点击「删除选中记录」：显示第 {_selectedRecordIndex + 1} 行 → 对应第 {origIdx + 1} 次通关记录，统一调用行内删除逻辑。");
        DeleteRecordByOriginalIndex(origIdx);
    }

    // ================================================================
    // 运行时 UI 辅助：创建文字/按钮（挂在某个父 Transform 下，RectTransform 基于锚点定位）
    // ================================================================
    private static Text MakeUIText(Transform parent, string name, string content,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax,
        int fontSize, TextAnchor anchor, Font font)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
        Text t = obj.AddComponent<Text>();
        t.text = content;
        t.font = font;
        t.fontSize = fontSize;
        t.alignment = anchor;
        t.color = Color.white;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        return t;
    }

    private static Button MakeUIButton(Transform parent, string name, string text,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredOffset,
        Vector2 sizeDelta, Font font, Vector2? pivot = null)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.sizeDelta = sizeDelta;
        // 经验：先改 pivot，再写 anchoredPosition。UGUI 改变 pivot 时会自动重算 anchoredPosition 保持世界坐标不变，
        // 若先设 anchoredPosition 再改 pivot，最终偏移值与预期会完全不同（典型症状：按钮跑出父矩形外被裁掉）。
        if (pivot.HasValue) rt.pivot = pivot.Value;
        rt.anchoredPosition = anchoredOffset;
        Image img = obj.AddComponent<Image>();
        img.color = new Color(0.9f, 0.55f, 0.15f);
        img.raycastTarget = true;  // 经验 1269998：动态创建 Button 的 Image 必须显式开启射线检测，缺省值理论为 true 但部分 Unity 版本在 AddComponent 后未初始化会导致按钮不可点
        Button btn = obj.AddComponent<Button>();
        ColorBlock cb = btn.colors;
        cb.normalColor = new Color(1f, 1f, 1f, 1f);
        cb.highlightedColor = new Color(0.92f, 0.92f, 0.92f, 1f);
        cb.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
        btn.colors = cb;
        btn.targetGraphic = img;

        GameObject lbl = new GameObject("Label");
        lbl.transform.SetParent(obj.transform, false);
        RectTransform lrt = lbl.AddComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
        Text lt = lbl.AddComponent<Text>();
        lt.text = text;
        lt.font = font;
        lt.fontSize = 18;
        lt.alignment = TextAnchor.MiddleCenter;
        lt.color = Color.white;
        return btn;
    }
}
