using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 游戏初始化器。
/// 挂载在场景中的空 GameObject 上，是整个程序的入口。
/// 负责创建所有游戏对象（相机、画布、网格、连线器、UI、控制器、Bot），
/// 并建立各组件之间的引用关系。所有游戏对象均通过代码动态创建，无需手动在 Inspector 中配置。
/// </summary>
public class GameInitializer : MonoBehaviour
{
    [Header("网格设置")]
    [SerializeField] private int gridRows = 8;   // 网格行数（必须与列数乘积为偶数，才能配对）
    [SerializeField] private int gridCols = 10;  // 网格列数
    [SerializeField] private float tileSize = 0.85f; // 每个瓦片在世界空间中的边长

    [Header("颜色设置")]
    // 图标可选的颜色数组，数组长度决定图标种类数（当不使用精灵图片时）
    [SerializeField] private Color[] tileColors = new Color[]
    {
        new Color(1f, 0.3f, 0.3f),    // 红色
        new Color(0.3f, 0.6f, 1f),    // 蓝色
        new Color(0.3f, 1f, 0.4f),    // 绿色
        new Color(1f, 0.9f, 0.2f),    // 黄色
        new Color(1f, 0.5f, 0.1f),    // 橙色
        new Color(0.7f, 0.3f, 1f),    // 紫色
        new Color(1f, 0.3f, 0.7f),    // 粉色
        new Color(0.3f, 1f, 0.9f),    // 青色
    };

    [Header("图片素材（可选）")]
    [Tooltip("拖入精灵图片，将取代色块显示。数量决定图标种类数")]
    // 可选的精灵图片数组。若不为空，则用图片替代纯色色块显示图标。
    [SerializeField] private Sprite[] tileSprites;

    /// <summary>
    /// Unity 生命周期：脚本启动时调用。
    /// 在此校验配置参数的合法性，然后执行游戏初始化。
    /// </summary>
    private void Start()
    {
        // 校验：网格总数必须为偶数，否则无法两两配对
        if ((gridRows * gridCols) % 2 != 0)
        {
            Debug.LogError($"行×列 ({gridRows}×{gridCols}) 必须为偶数才能配对！使用默认 8×10");
            gridRows = 8;
            gridCols = 10;
        }
        // 校验：至少需要一种颜色
        if (tileColors == null || tileColors.Length == 0)
        {
            Debug.LogError("至少需要1种颜色！使用默认颜色");
            tileColors = new Color[] { new Color(1f, 0.3f, 0.3f) };
        }
        Debug.Log("=== GameInitializer 开始初始化 ===");
        SetupGame();
    }

    /// <summary>
    /// 创建并组装整个游戏的所有对象和组件。
    /// 创建顺序很重要：被依赖的对象需要先创建。
    /// </summary>
    private void SetupGame()
    {
        // ---------- 1. 相机设置 ----------
        // 获取或创建主相机，设置为正交投影，并调整视野大小以容纳整个网格
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            GameObject camObj = new GameObject("MainCamera");
            mainCamera = camObj.AddComponent<Camera>();
            camObj.tag = "MainCamera";
        }
        mainCamera.orthographic = true;
        // 正交视野大小根据网格最大维度计算，保证网格能完整显示
        mainCamera.orthographicSize = Mathf.Max(gridRows, gridCols) * 0.6f;
        mainCamera.transform.position = new Vector3(0, 0, -10);
        // 4K 显示器画面优化：纯黑背景 + SolidColor 清屏，
        // 避免 Skybox/Depth 模式下非整数缩放导致瓦片边缘出现 1px 白线与整体视觉发虚。
        mainCamera.clearFlags = CameraClearFlags.SolidColor;
        mainCamera.backgroundColor = Color.black;

        // ---------- 2. 字体获取 ----------
        // 获取 Unity 内置字体，用于 UI 文字显示
        Font defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (defaultFont == null)
            Debug.LogWarning("LegacyRuntime.ttf 字体未找到，UI 可能显示异常");

        // ---------- 3. 创建 UI 画布 ----------
        GameObject canvasObj = CreateCanvas();

        // ---------- 4. 创建瓦片预制体和容器 ----------
        GameObject tilePrefab = CreateTilePrefab();        // 创建瓦片预制体（含主精灵、高亮精灵、碰撞器、Tile 脚本）
        GameObject tileContainer = new GameObject("TileContainer"); // 所有瓦片实例的父对象，方便统一管理

        // ---------- 5. 创建网格管理器 ----------
        GameObject gridObj = new GameObject("GridManager");
        GridManager gridManager = gridObj.AddComponent<GridManager>();
        gridManager.SetGridSettings(gridRows, gridCols, tileSize, Vector2.zero); // 设置网格参数
        gridManager.SetTilePrefabAndContainer(tilePrefab, tileContainer.transform); // 设置预制体和容器
        gridManager.SetTileColors(tileColors);    // 设置颜色数组
        gridManager.SetTileSprites(tileSprites);   // 设置精灵数组（可空）

        // ---------- 6. 创建连线绘制器 ----------
        GameObject linePrefab = CreateLineRendererPrefab(); // 创建连线预制体
        GameObject lineObj = new GameObject("LineDrawer");
        LineDrawer lineDrawer = lineObj.AddComponent<LineDrawer>();
        lineDrawer.SetLinePrefab(linePrefab);

        // ---------- 7. 创建游戏控制器（必须早于 UI 创建！）----------
        // UIManager.SetUIReferences 内部会订阅 GameController.Instance.OnLevelCleared（用于通关自动写记录），
        // 若 GameController 在 UI 之后才创建，订阅时 Instance 为 null 会被跳过 → 通关记录永远写不进去（本 bug 根因）。
        // GameController.Awake 只设置单例、无依赖；依赖在下面 UI 创建完成后通过 SetReferences 注入。
        GameObject gameObj = new GameObject("GameController");
        GameController gameController = gameObj.AddComponent<GameController>();

        // ---------- 8. 创建 UI 管理器并搭建界面 ----------
        GameObject uiObj = new GameObject("UIManager");
        UIManager uiManager = uiObj.AddComponent<UIManager>();
        SetupUI(uiManager, canvasObj, defaultFont); // 创建所有 UI 元素并绑定到 UIManager（此时 GameController.Instance 已存在，OnLevelCleared 订阅成功）

        // 将网格管理器、连线绘制器、UI 管理器注入到游戏控制器
        gameController.SetReferences(gridManager, lineDrawer, uiManager);

        // ---------- 9. 创建 Bot 控制器 ----------
        GameObject botObj = new GameObject("BotController");
        botObj.AddComponent<BotController>();

        // ---------- 10. 创建关卡记录管理器（持久化通关记录） ----------
        GameObject recordObj = new GameObject("LevelRecordManager");
        recordObj.AddComponent<LevelRecordManager>();

        Debug.Log("=== GameInitializer 初始化完成 ===");
    }

    /// <summary>
    /// 创建 UI 画布及事件系统。
    /// 画布使用 ScreenSpaceOverlay 模式，并配合 CanvasScaler 实现分辨率自适应。
    /// </summary>
    /// <returns>画布 GameObject</returns>
    private GameObject CreateCanvas()
    {
        // 若场景中已存在 Canvas（避免重复创建），直接返回
        GameObject existing = GameObject.Find("Canvas");
        if (existing != null) return existing;

        GameObject obj = new GameObject("Canvas");
        Canvas canvas = obj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; // 画布覆盖在屏幕之上

        // 配置缩放器：以 1920×1080 为参考分辨率，随屏幕大小等比缩放
        CanvasScaler scaler = obj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        // 4K 显示器分辨率模糊修复（经验 718155：必须显式设置宽高匹配 0.5，
        // 缺省值下非 16:9 窗口会出现 scaleFactor 非整数倍导致文字/按钮 bilinear 模糊）。
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        scaler.referencePixelsPerUnit = 100f;
        obj.AddComponent<GraphicRaycaster>(); // 启用 UI 射线检测（用于按钮点击）

        // 创建事件系统（UI 交互必需）
        GameObject eventSystem = GameObject.Find("EventSystem");
        if (eventSystem == null)
        {
            eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystem.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        return obj;
    }

    /// <summary>
    /// 创建瓦片预制体。
    /// 结构：TilePrefab（根，含 BoxCollider2D 和 Tile 脚本）
    ///       ├── MainSprite（主显示精灵）
    ///       └── HighlightSprite（高亮叠加精灵，默认隐藏）
    /// </summary>
    /// <returns>瓦片预制体 GameObject</returns>
    private GameObject CreateTilePrefab()
    {
        GameObject prefab = new GameObject("TilePrefab");
        prefab.SetActive(false); // 预制体默认禁用，实例化后再激活

        // 创建主精灵子对象，用于显示图标颜色或图片
        GameObject mainObj = new GameObject("MainSprite");
        mainObj.transform.SetParent(prefab.transform, false);
        SpriteRenderer mainRenderer = mainObj.AddComponent<SpriteRenderer>();
        mainRenderer.sprite = CreateDefaultSprite(new Color(0.8f, 0.8f, 0.8f)); // 默认灰色

        // 创建高亮精灵子对象，用于选中时显示黄色高亮
        GameObject highlightObj = new GameObject("HighlightSprite");
        highlightObj.transform.SetParent(prefab.transform, false);
        SpriteRenderer highlightRenderer = highlightObj.AddComponent<SpriteRenderer>();
        highlightRenderer.sprite = CreateDefaultSprite(new Color(1f, 1f, 0.5f)); // 半透明黄色
        highlightRenderer.sortingOrder = 1; // 高亮层在主精灵之上
        highlightRenderer.gameObject.SetActive(false); // 默认隐藏

        // 添加 2D 碰撞器，用于检测鼠标点击（OnMouseDown 依赖碰撞器）
        BoxCollider2D collider = prefab.AddComponent<BoxCollider2D>();

        // 添加 Tile 脚本并注入渲染器引用
        Tile tile = prefab.AddComponent<Tile>();
        tile.SetRenderers(mainRenderer, highlightRenderer);

        return prefab;
    }

    /// <summary>
    /// 程序化生成一个纯色的精灵（Sprite）。
    /// 通过创建 64×64 的纹理并填充单色来生成，用于瓦片的默认显示。
    /// </summary>
    /// <param name="color">精灵颜色</param>
    /// <returns>生成的 Sprite</returns>
    private Sprite CreateDefaultSprite(Color color)
    {
        int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[size * size];

        // 将所有像素填充为指定颜色
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = color;

        tex.SetPixels(pixels);
        tex.Apply();
        // 创建 Sprite，中心点为 (0.5, 0.5)，像素与单位比例为 size
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    /// <summary>
    /// 创建连线预制体。
    /// 使用 LineRenderer 组件，配置金色线条的材质、颜色和宽度。
    /// </summary>
    /// <returns>连线预制体 GameObject</returns>
    private GameObject CreateLineRendererPrefab()
    {
        GameObject prefab = new GameObject("LinePrefab");
        prefab.SetActive(false);

        LineRenderer lr = prefab.AddComponent<LineRenderer>();
        lr.material = new Material(Shader.Find("Sprites/Default")); // 使用默认 Sprite 着色器
        lr.startColor = new Color(1f, 0.8f, 0.2f); // 金色起点
        lr.endColor = new Color(1f, 0.8f, 0.2f);   // 金色终点
        lr.startWidth = 0.08f; // 起点宽度
        lr.endWidth = 0.08f;   // 终点宽度
        lr.positionCount = 0;  // 初始无点
        lr.useWorldSpace = true; // 使用世界坐标，使连线与瓦片位置对齐

        return prefab;
    }

    /// <summary>
    /// 搭建完整的 UI 界面，包括三部分：
    /// 1. 标题界面（TitlePanel）— 游戏开始前显示
    /// 2. 游戏界面容器（GameHud）— 游戏进行时显示
    /// 3. 结束面板（GameOverPanel）— 嵌套在 GameHud 中，过关时显示
    /// </summary>
    private void SetupUI(UIManager uiManager, GameObject canvas, Font font)
    {
        Transform canvasT = canvas.transform;

        // ---------- 标题界面 ----------
        // 全屏深色背景面板，游戏启动时显示
        GameObject titlePanel = new GameObject("TitlePanel");
        titlePanel.transform.SetParent(canvasT, false);
        RectTransform titleRect = titlePanel.AddComponent<RectTransform>();
        titleRect.anchorMin = Vector2.zero;    // 锚定左下角
        titleRect.anchorMax = Vector2.one;      // 锚定右上角，实现铺满全屏
        titleRect.sizeDelta = Vector2.zero;
        titleRect.anchoredPosition = Vector2.zero;
        Image titleBg = titlePanel.AddComponent<Image>();
        titleBg.color = new Color(0.15f, 0.15f, 0.25f); // 深蓝灰背景

        // 标题文字 "连 连 看"，位于上方 70% 处
        Text titleText = MakeText("TitleText", titlePanel.transform, "连 连 看",
            new Vector2(0.5f, 0.7f), new Vector2(0.5f, 0.7f), Vector2.zero, 400, 100,
            72, TextAnchor.MiddleCenter, font);

        // "开始游戏" 按钮，位于中间 50% 处
        Button startBtn = MakeButton("StartButton", titlePanel.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, 200, 55,
            "开始游戏", font);
        startBtn.GetComponent<Image>().color = new Color(0.2f, 0.7f, 0.3f); // 绿色

        // "结束游戏" 按钮，位于开始按钮下方 75 像素处
        Button titleQuitBtn = MakeButton("TitleQuitButton", titlePanel.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -75), 200, 55,
            "结束游戏", font);
        titleQuitBtn.GetComponent<Image>().color = new Color(0.7f, 0.3f, 0.3f); // 红色

        // "关卡设计" 按钮：位于开始按钮上方 75 像素处
        Button titleLevelDesignBtn = MakeButton("TitleLevelDesignButton", titlePanel.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 75), 200, 55,
            "关卡设计", font);
        titleLevelDesignBtn.GetComponent<Image>().color = new Color(0.9f, 0.55f, 0.15f); // 橙色

        // ---------- 游戏界面容器 ----------
        // 包含所有游戏中的 UI 元素，与标题界面互斥显示
        GameObject gameHud = new GameObject("GameHud");
        gameHud.transform.SetParent(canvasT, false);
        RectTransform hudRect = gameHud.AddComponent<RectTransform>();
        hudRect.anchorMin = Vector2.zero;
        hudRect.anchorMax = Vector2.one;
        hudRect.sizeDelta = Vector2.zero;
        hudRect.anchoredPosition = Vector2.zero;
        Transform hudT = gameHud.transform;

        // HUD 顶部三文字统一策略：距顶 20 像素（yTop = 屏幕高度 - 20），高度 44 像素，
        // 按「左 28% / 中 40% / 右 28%」分栏，互不遮挡且不会因 pivot 错误出屏幕上缘。
        // 分数文字：左上角 (20, 1060 on 1080p)
        Text scoreText = MakeText("ScoreText", hudT, "分数: 0",
            new Vector2(0, 1), new Vector2(0.28f, 1), Vector2.zero, 0, 0,
            28, TextAnchor.MiddleLeft, font);
        RectTransform scoreRect = scoreText.GetComponent<RectTransform>();
        scoreRect.pivot = new Vector2(0, 1);
        scoreRect.offsetMin = new Vector2(20, -64); // 下 = 顶部 - 44px 高 - 20px 距顶 = -64
        scoreRect.offsetMax = new Vector2(-20, -20);

        // 时间文字：右上角 (1920-20, 1060 on 1080p)，右分栏 72%~100%
        Text timerText = MakeText("TimerText", hudT, "时间: 00:00",
            new Vector2(0.72f, 1), new Vector2(1, 1), Vector2.zero, 0, 0,
            24, TextAnchor.MiddleRight, font);
        RectTransform timerRect = timerText.GetComponent<RectTransform>();
        timerRect.pivot = new Vector2(1, 1);
        timerRect.offsetMin = new Vector2(20, -64);
        timerRect.offsetMax = new Vector2(-20, -20);

        // 剩余对数文字：顶部正中间 (960 中心)，中分栏 32%~68%
        Text pairsText = MakeText("PairsText", hudT, "剩余对数: 0",
            new Vector2(0.32f, 1), new Vector2(0.68f, 1), Vector2.zero, 0, 0,
            22, TextAnchor.MiddleCenter, font);
        RectTransform pairsRect = pairsText.GetComponent<RectTransform>();
        pairsRect.pivot = new Vector2(0.5f, 1);
        pairsRect.offsetMin = new Vector2(0, -64);
        pairsRect.offsetMax = new Vector2(0, -20);

        // "重新开始" 按钮：底部偏左（y=25，高 40，Bot 演示在 y=80 → 完全不重叠）
        Button restartBtn = MakeButton("RestartButton", hudT,
            new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-170, 25), 150, 44,
            "重新开始", font);

        // "重新排列" 按钮：底部偏右（与重新开始对称，水平间距 40 像素）
        Button shuffleBtn = MakeButton("ShuffleButton", hudT,
            new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(170, 25), 150, 44,
            "重新排列", font);

        // "Bot 演示" 按钮：底部正中间（y=85，高 44，顶部 y=85+22=107 > 重排底部 y=25-22=3）
        Button botBtn = MakeButton("BotButton", hudT,
            new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 85), 160, 44,
            "Bot 演示", font);
        botBtn.GetComponent<Image>().color = new Color(0.2f, 0.7f, 0.3f); // 绿色
        Text botBtnText = botBtn.GetComponentInChildren<Text>();

        // "返回标题" 按钮：右上角时间文字正下方（距顶 80，右边缘缩 20px）
        // - 时间文字底部 y = 1016；按钮顶部 y = 1000 → 间距 16px，无重叠
        // - 右边缘 1900，不会超出屏幕（1920 宽）
        Button backToTitleBtnInGame = MakeButton("BackToTitleButton", hudT,
            new Vector2(1, 1), new Vector2(1, 1), Vector2.zero, 170, 48,
            "返回标题", font);
        backToTitleBtnInGame.GetComponent<Image>().color = new Color(0.3f, 0.45f, 0.75f);
        RectTransform b2tRect = backToTitleBtnInGame.GetComponent<RectTransform>();
        b2tRect.pivot = new Vector2(1, 1); // 右上角对齐锚点，保证按钮整体不会超出右侧
        b2tRect.anchoredPosition = new Vector2(-20, -84);

        // ---------- 结束面板 ----------
        // 全屏半透明黑色覆盖层，嵌套在 GameHud 内，默认隐藏，过关时显示
        GameObject panelObj = new GameObject("GameOverPanel");
        panelObj.transform.SetParent(hudT, false);
        RectTransform panelRect = panelObj.AddComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.sizeDelta = Vector2.zero;
        panelRect.anchoredPosition = Vector2.zero;
        Image panelBg = panelObj.AddComponent<Image>();
        panelBg.color = new Color(0, 0, 0, 0.7f); // 半透明黑色
        panelObj.SetActive(false); // 默认隐藏

        // 过关提示文字，显示用时信息
        Text overText = MakeText("GameOverText", panelObj.transform, "",
            new Vector2(0.5f, 0.65f), new Vector2(0.5f, 0.65f), Vector2.zero, 350, 100,
            44, TextAnchor.MiddleCenter, font);

        // "再来一局" 按钮
        Button overRestartBtn = MakeButton("OverRestartButton", panelObj.transform,
            new Vector2(0.5f, 0.4f), new Vector2(0.5f, 0.4f), Vector2.zero, 160, 50,
            "再来一局", font);

        // "结束游戏" 按钮（GameOver 面板的第二键）→ 改为「返回标题」：通关后玩家可一键回到标题页
        // （标题页红色按钮仍然是真正的"结束游戏 / Application.Quit"，功能区分开避免误点）
        Button overQuitBtn = MakeButton("OverBackToTitleButton", panelObj.transform,
            new Vector2(0.5f, 0.4f), new Vector2(0.5f, 0.4f), new Vector2(0, -70), 160, 50,
            "返回标题", font);
        overQuitBtn.GetComponent<Image>().color = new Color(0.3f, 0.5f, 0.75f); // 蓝灰色，区别于"再来一局"

        // ---------- 难度指标面板 ----------
        // 半透明深色面板，显示 8 个指标和综合难度，默认隐藏。
        // 布局说明（参考分辨率 1920×1080）：
        //   - 锚定左侧垂直居中 (0, 0.5)，pivot 左中 (0, 0.5)，从左边缘向右展开
        //   - 尺寸 396×580，覆盖屏幕 y∈[250, 830]
        //   - 顶部留出空间给顶部 3 文字 (y=[1016,1060]) 和难度按钮 (y=[954,994])，间距 ≥124
        //   - 底部留出空间给 3 操作键 (y∈[3~131])，间距 ≥119
        //   - 右侧 x=416 远离游戏网格 (x≥578)，避免遮挡棋盘
        GameObject diffPanel = new GameObject("DifficultyPanel");
        diffPanel.transform.SetParent(hudT, false);
        RectTransform diffRect = diffPanel.AddComponent<RectTransform>();
        diffRect.anchorMin = new Vector2(0, 0.5f);
        diffRect.anchorMax = new Vector2(0, 0.5f);
        diffRect.pivot = new Vector2(0, 0.5f);
        diffRect.anchoredPosition = new Vector2(20, 0);
        diffRect.sizeDelta = new Vector2(396, 580);
        Image diffBg = diffPanel.AddComponent<Image>();
        diffBg.color = new Color(0.1f, 0.1f, 0.2f, 0.92f);
        diffPanel.AddComponent<RectMask2D>();
        diffPanel.SetActive(false);

        // 难度指标文字：完全填充面板内部，留 18px 内边距
        Text diffText = MakeText("DifficultyText", diffPanel.transform, "",
            new Vector2(0, 0), new Vector2(1, 1), Vector2.zero, 0, 0,
            14, TextAnchor.UpperLeft, font);
        RectTransform diffTextRect = diffText.GetComponent<RectTransform>();
        diffTextRect.pivot = new Vector2(0.5f, 0.5f);
        diffTextRect.offsetMin = new Vector2(18, 18);
        diffTextRect.offsetMax = new Vector2(-18, -18);
        diffText.horizontalOverflow = HorizontalWrapMode.Wrap;
        diffText.verticalOverflow = VerticalWrapMode.Truncate;
        diffText.lineSpacing = 1.15f;

        // ---------- 难度指标开关按钮 ----------
        // 左上角：锚定 (0,1) pivot=(0,1)，距左 20px，距顶 86px，
        // 顶部 3 行文字底部 y≈1016，难度按钮顶部 y≈994 → 间距 22px 安全。
        Button diffBtn = MakeButton("DifficultyButton", hudT,
            new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, 150, 40,
            "难度指标", font);
        RectTransform diffBtnRect = diffBtn.GetComponent<RectTransform>();
        diffBtnRect.pivot = new Vector2(0, 1);
        diffBtnRect.anchoredPosition = new Vector2(20, -86);
        diffBtn.GetComponent<Image>().color = new Color(0.5f, 0.3f, 0.8f);

        // 将所有 UI 引用传递给 UIManager，并绑定按钮事件
        // —— 关卡设计器相关 UI（LevelDesignPanel / LevelSelectorPanel / RecordPanel）
        Transform designT, selectorT, recordT;
        InputField rowsInput, colsInput, typesInput, genCountInput;
        Text[] metricLabels; InputField[] metricInputs; Dropdown[] metricGradeDrops; Toggle[] metricUseGradeToggles;
        Text ddPreviewText; Button generateBtn; Image genProgressFill; Text genProgressLabel;
        GameObject levelListContainer; Button backToTitleBtn2, backToDesignBtn, backToTitleBtn3, refreshListBtn, recordsBtn, recordsCloseBtn, recordDeleteBtn;
        GameObject recordsListContainer; Text currentRecordLevelLabel;
        GameObject levelDesignPanel, levelSelectorPanel, recordPanel;
        Button recordBackBtn;
        SetupLevelDesignerUI(canvasT, font, out levelDesignPanel, out designT,
            out rowsInput, out colsInput, out typesInput, out genCountInput,
            out metricLabels, out metricInputs, out metricGradeDrops, out metricUseGradeToggles,
            out ddPreviewText, out generateBtn, out genProgressFill, out genProgressLabel, out backToTitleBtn2,
            out levelSelectorPanel, out selectorT, out levelListContainer, out backToDesignBtn, out backToTitleBtn3, out refreshListBtn,
            out recordPanel, out recordsBtn, out recordsCloseBtn, out recordBackBtn, out recordsListContainer, out recordDeleteBtn, out currentRecordLevelLabel);

        uiManager.SetUIReferences(scoreText, timerText, panelObj, overText,
            restartBtn, shuffleBtn, overRestartBtn, botBtn, botBtnText,
            titlePanel, startBtn, titleQuitBtn, overQuitBtn, gameHud,
            diffPanel, diffText, diffBtn,
            titleLevelDesignBtn,
            levelDesignPanel, rowsInput, colsInput, typesInput, genCountInput,
            metricLabels, metricInputs, metricGradeDrops, metricUseGradeToggles,
            ddPreviewText, generateBtn, genProgressFill, genProgressLabel, backToTitleBtn2,
            levelSelectorPanel, levelListContainer, backToDesignBtn, backToTitleBtn3, refreshListBtn,
            recordPanel, recordsBtn, recordsCloseBtn, recordBackBtn, recordsListContainer, recordDeleteBtn, currentRecordLevelLabel,
            pairsText: pairsText,
            hudBackToTitle: backToTitleBtnInGame);
    }

    /// <summary>
    /// 通用文字创建方法。
    /// 创建一个带 RectTransform 和 Text 组件的 GameObject。
    /// </summary>
    /// <param name="name">对象名</param>
    /// <param name="parent">父节点</param>
    /// <param name="content">初始文字内容</param>
    /// <param name="anchorMin">锚点最小值（百分比）</param>
    /// <param name="anchorMax">锚点最大值（百分比）</param>
    /// <param name="pos">锚点偏移位置</param>
    /// <param name="w">宽度（0 表示用锚点撑开）</param>
    /// <param name="h">高度</param>
    /// <param name="fontSize">字体大小</param>
    /// <param name="alignment">文字对齐方式</param>
    /// <param name="font">字体</param>
    /// <returns>创建的 Text 组件</returns>
    private Text MakeText(string name, Transform parent, string content,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, float w, float h,
        int fontSize, TextAnchor alignment, Font font)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);

        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.anchoredPosition = pos;
        rect.sizeDelta = new Vector2(w, h);

        Text text = obj.AddComponent<Text>();
        text.text = content;
        if (font != null) text.font = font;
        text.fontSize = fontSize;
        text.color = Color.white;
        text.alignment = alignment;
        // 允许文字超出边界显示，防止被裁剪
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;

        return text;
    }

    /// <summary>
    /// 通用按钮创建方法。
    /// 创建一个带 Image（背景）、Button（交互）和子 Text（文字）的 GameObject。
    /// </summary>
    /// <param name="name">对象名</param>
    /// <param name="parent">父节点</param>
    /// <param name="anchorMin">锚点最小值</param>
    /// <param name="anchorMax">锚点最大值</param>
    /// <param name="pos">锚点偏移位置</param>
    /// <param name="w">按钮宽度</param>
    /// <param name="h">按钮高度</param>
    /// <param name="buttonText">按钮文字</param>
    /// <param name="font">字体</param>
    /// <returns>创建的 Button 组件</returns>
    private Button MakeButton(string name, Transform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, float w, float h,
        string buttonText, Font font)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);

        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.anchoredPosition = pos;
        rect.sizeDelta = new Vector2(w, h);
        rect.localScale = Vector3.one;

        // 按钮背景图片，默认蓝色
        Image image = obj.AddComponent<Image>();
        image.color = new Color(0.3f, 0.6f, 1f);

        Button button = obj.AddComponent<Button>();

        // 创建按钮文字子对象，铺满整个按钮区域
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(obj.transform, false);

        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        // 经验 1271918：四周加内边距，避免按钮文字贴边、被 Image 边缘裁掉
        textRect.offsetMin = new Vector2(6, 4);
        textRect.offsetMax = new Vector2(-6, -4);

        Text text = textObj.AddComponent<Text>();
        text.text = buttonText;
        if (font != null) text.font = font;
        text.fontSize = 20;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleCenter;

        return button;
    }

    /// <summary>
    /// 创建数字输入框（InputField）。
    /// 返回 InputField 组件（其子对象 Placeholder/Text 自动创建）。
    /// ContentType = IntegerNumber，供行/列/类型数/关卡数量使用。
    /// </summary>
    private InputField MakeInputField(string name, Transform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, float w, float h,
        string placeholderText, Font font, int fontSize = 18)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.anchoredPosition = pos;
        rect.sizeDelta = new Vector2(w, h);

        // 背景
        Image bg = obj.AddComponent<Image>();
        bg.color = new Color(0.12f, 0.12f, 0.18f, 0.95f);

        InputField input = obj.AddComponent<InputField>();
        input.contentType = InputField.ContentType.IntegerNumber;
        input.lineType = InputField.LineType.SingleLine;
        input.characterLimit = 4;

        // 占位文字
        GameObject phObj = new GameObject("Placeholder");
        phObj.transform.SetParent(obj.transform, false);
        RectTransform phRect = phObj.AddComponent<RectTransform>();
        phRect.anchorMin = Vector2.zero; phRect.anchorMax = Vector2.one;
        phRect.offsetMin = new Vector2(10, 0); phRect.offsetMax = new Vector2(-10, 0);
        Text phText = phObj.AddComponent<Text>();
        if (font != null) phText.font = font;
        phText.fontSize = fontSize;
        phText.color = new Color(1, 1, 1, 0.4f);
        phText.text = placeholderText;
        phText.alignment = TextAnchor.MiddleLeft;
        input.placeholder = phText;

        // 实际文字
        GameObject txtObj = new GameObject("Text");
        txtObj.transform.SetParent(obj.transform, false);
        RectTransform txRect = txtObj.AddComponent<RectTransform>();
        txRect.anchorMin = Vector2.zero; txRect.anchorMax = Vector2.one;
        txRect.offsetMin = new Vector2(10, 0); txRect.offsetMax = new Vector2(-10, 0);
        Text tx = txtObj.AddComponent<Text>();
        if (font != null) tx.font = font;
        tx.fontSize = fontSize;
        tx.color = Color.white;
        tx.supportRichText = true;
        tx.alignment = TextAnchor.MiddleLeft;
        input.textComponent = tx;

        return input;
    }

    /// <summary>
    /// 创建下拉框（Dropdown）。选项由 options 参数传入。
    /// 返回 Dropdown 组件，用于难度等级选择等。
    /// </summary>
    private Dropdown MakeDropdown(string name, Transform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, float w, float h,
        List<string> options, Font font, int fontSize = 16)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.anchoredPosition = pos;
        rect.sizeDelta = new Vector2(w, h);

        Image bg = obj.AddComponent<Image>();
        bg.color = new Color(0.12f, 0.12f, 0.18f, 0.95f);

        Dropdown dd = obj.AddComponent<Dropdown>();

        // Label
        GameObject lblObj = new GameObject("Label");
        lblObj.transform.SetParent(obj.transform, false);
        RectTransform lblRect = lblObj.AddComponent<RectTransform>();
        lblRect.anchorMin = Vector2.zero; lblRect.anchorMax = Vector2.one;
        lblRect.offsetMin = new Vector2(10, 0); lblRect.offsetMax = new Vector2(-h - 4, 0);
        Text lbl = lblObj.AddComponent<Text>();
        if (font != null) lbl.font = font;
        lbl.fontSize = fontSize;
        lbl.color = Color.white;
        lbl.alignment = TextAnchor.MiddleLeft;
        dd.captionText = lbl;

        // Arrow
        GameObject arrowObj = new GameObject("Arrow");
        arrowObj.transform.SetParent(obj.transform, false);
        RectTransform arrRect = arrowObj.AddComponent<RectTransform>();
        arrRect.anchorMin = new Vector2(1, 0.5f);
        arrRect.anchorMax = new Vector2(1, 0.5f);
        arrRect.pivot = new Vector2(1, 0.5f);
        arrRect.sizeDelta = new Vector2(h * 0.6f, h * 0.6f);
        arrRect.anchoredPosition = new Vector2(-8, 0);
        Text arrTxt = arrowObj.AddComponent<Text>();
        if (font != null) arrTxt.font = font;
        arrTxt.text = "▼";
        arrTxt.fontSize = fontSize;
        arrTxt.alignment = TextAnchor.MiddleCenter;
        arrTxt.color = Color.white;

        // Template
        GameObject tplObj = new GameObject("Template");
        tplObj.transform.SetParent(obj.transform, false);
        RectTransform tplRect = tplObj.AddComponent<RectTransform>();
        tplRect.anchorMin = new Vector2(0, 0);
        tplRect.anchorMax = new Vector2(1, 0);
        tplRect.pivot = new Vector2(0.5f, 1);
        tplRect.sizeDelta = new Vector2(0, 180);
        tplRect.anchoredPosition = new Vector2(0, 0);
        Image tplBg = tplObj.AddComponent<Image>();
        tplBg.color = new Color(0.08f, 0.08f, 0.12f, 0.98f);
        tplObj.AddComponent<RectMask2D>();

        // Content container
        GameObject contentObj = new GameObject("Content");
        contentObj.transform.SetParent(tplObj.transform, false);
        RectTransform contentRect = contentObj.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 1);
        contentRect.anchorMax = new Vector2(1, 1);
        contentRect.pivot = new Vector2(0.5f, 1);
        contentRect.sizeDelta = new Vector2(0, 180);
        contentRect.anchoredPosition = Vector2.zero;

        // Item
        GameObject itemObj = new GameObject("Item");
        itemObj.transform.SetParent(contentObj.transform, false);
        RectTransform itemRect = itemObj.AddComponent<RectTransform>();
        itemRect.anchorMin = new Vector2(0, 1);
        itemRect.anchorMax = new Vector2(1, 1);
        itemRect.pivot = new Vector2(0.5f, 1);
        itemRect.sizeDelta = new Vector2(0, 32);
        itemRect.anchoredPosition = Vector2.zero;
        Image itemBg = itemObj.AddComponent<Image>();
        itemBg.color = new Color(0.15f, 0.15f, 0.22f, 1);
        Toggle itemToggle = itemObj.AddComponent<Toggle>();

        GameObject iBgObj = new GameObject("ItemBackground");
        iBgObj.transform.SetParent(itemObj.transform, false);
        RectTransform iBgRect = iBgObj.AddComponent<RectTransform>();
        iBgRect.anchorMin = Vector2.zero; iBgRect.anchorMax = Vector2.one;
        iBgRect.offsetMin = Vector2.zero; iBgRect.offsetMax = Vector2.zero;
        Image iBg = iBgObj.AddComponent<Image>();
        iBg.color = new Color(0.3f, 0.4f, 0.9f);
        itemToggle.targetGraphic = iBg;

        GameObject iLabelObj = new GameObject("ItemLabel");
        iLabelObj.transform.SetParent(itemObj.transform, false);
        RectTransform iLabelRect = iLabelObj.AddComponent<RectTransform>();
        iLabelRect.anchorMin = Vector2.zero; iLabelRect.anchorMax = Vector2.one;
        iLabelRect.offsetMin = new Vector2(10, 0); iLabelRect.offsetMax = new Vector2(-10, 0);
        Text iLabel = iLabelObj.AddComponent<Text>();
        if (font != null) iLabel.font = font;
        iLabel.fontSize = fontSize;
        iLabel.color = Color.white;
        iLabel.alignment = TextAnchor.MiddleLeft;

        itemToggle.graphic = null;
        dd.template = tplRect;
        dd.captionText = lbl;
        dd.itemText = iLabel;

        dd.options = new List<Dropdown.OptionData>();
        foreach (var s in options ?? new List<string>())
            dd.options.Add(new Dropdown.OptionData(s));
        dd.value = 0;
        tplObj.SetActive(false); // Dropdown 要求初始隐藏 template

        return dd;
    }

    /// <summary>
    /// 创建一个通用开关（Toggle），左侧文字 + 右侧勾选框。
    /// </summary>
    private Toggle MakeToggle(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 pos, float w, float h, string label, Font font)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = anchorMin; rect.anchorMax = anchorMax;
        rect.anchoredPosition = pos; rect.sizeDelta = new Vector2(w, h);

        // 文字
        Text lbl = MakeText("Label", obj.transform, label,
            new Vector2(0, 0), new Vector2(0.5f, 1), Vector2.zero, 0, 0,
            14, TextAnchor.MiddleLeft, font);

        // 勾选框
        GameObject ckObj = new GameObject("Checkmark");
        ckObj.transform.SetParent(obj.transform, false);
        RectTransform ckRect = ckObj.AddComponent<RectTransform>();
        ckRect.anchorMin = new Vector2(0.6f, 0.5f);
        ckRect.anchorMax = new Vector2(0.6f, 0.5f);
        ckRect.sizeDelta = new Vector2(h * 0.8f, h * 0.8f);
        ckRect.anchoredPosition = Vector2.zero;
        Image ckBg = ckObj.AddComponent<Image>();
        ckBg.color = new Color(0.12f, 0.12f, 0.18f, 1);

        Toggle tg = obj.AddComponent<Toggle>();
        tg.targetGraphic = ckBg;
        tg.graphic = null;

        // 勾选图像
        GameObject innerObj = new GameObject("Inner");
        innerObj.transform.SetParent(ckObj.transform, false);
        RectTransform inRect = innerObj.AddComponent<RectTransform>();
        inRect.anchorMin = Vector2.zero; inRect.anchorMax = Vector2.one;
        inRect.offsetMin = new Vector2(4, 4); inRect.offsetMax = new Vector2(-4, -4);
        Image inImg = innerObj.AddComponent<Image>();
        inImg.color = new Color(0.2f, 0.8f, 0.4f);
        tg.graphic = inImg;

        return tg;
    }

    /// <summary>
    /// 创建关卡设计器相关的三块界面：LevelDesignPanel、LevelSelectorPanel、RecordPanel。
    /// 三块均为画布直接子对象，默认隐藏，与标题界面 / GameHud 互斥切换。
    /// </summary>
    private void SetupLevelDesignerUI(Transform canvasT, Font font,
        out GameObject levelDesignPanel, out Transform designT,
        out InputField rowsInput, out InputField colsInput, out InputField typesInput, out InputField genCountInput,
        out Text[] metricLabels, out InputField[] metricInputs, out Dropdown[] metricGradeDrops, out Toggle[] metricUseGradeToggles,
        out Text ddPreviewText, out Button generateBtn, out Image genProgressFill, out Text genProgressLabel, out Button backToTitleBtn2,
        out GameObject levelSelectorPanel, out Transform selectorT,
        out GameObject levelListContainer, out Button backToDesignBtn, out Button backToTitleBtn3, out Button refreshListBtn,
        out GameObject recordPanel, out Button recordsBtn, out Button recordsCloseBtn, out Button recordBackBtn,
        out GameObject recordsListContainer, out Button recordDeleteBtn, out Text currentRecordLevelLabel)
    {
        List<string> gradeOptions = new List<string>(DifficultyGradeUtil.GradeNames);

        // ========================== LevelDesignPanel ==========================
        levelDesignPanel = new GameObject("LevelDesignPanel");
        levelDesignPanel.transform.SetParent(canvasT, false);
        RectTransform dr = levelDesignPanel.AddComponent<RectTransform>();
        dr.anchorMin = Vector2.zero; dr.anchorMax = Vector2.one;
        dr.sizeDelta = Vector2.zero; dr.anchoredPosition = Vector2.zero;
        Image dbg = levelDesignPanel.AddComponent<Image>();
        dbg.color = new Color(0.12f, 0.12f, 0.20f);
        levelDesignPanel.SetActive(false);
        designT = levelDesignPanel.transform;

        // 标题
        MakeText("DesignPanelTitle", designT, "关卡设计",
            new Vector2(0.5f, 0.95f), new Vector2(0.5f, 0.95f), Vector2.zero, 600, 50,
            36, TextAnchor.MiddleCenter, font);

        // 基础参数：行/列/类型数/生成数量
        float baseY = 0.86f;
        MakeText("LblRows", designT, "行数",
            new Vector2(0.15f, baseY), new Vector2(0.15f, baseY), Vector2.zero, 60, 28,
            18, TextAnchor.MiddleRight, font);
        rowsInput = MakeInputField("RowsInput", designT,
            new Vector2(0.20f, baseY), new Vector2(0.20f, baseY), Vector2.zero, 100, 32,
            "8", font); rowsInput.text = "8";
        rowsInput.contentType = InputField.ContentType.IntegerNumber;

        MakeText("LblCols", designT, "列数",
            new Vector2(0.35f, baseY), new Vector2(0.35f, baseY), Vector2.zero, 60, 28,
            18, TextAnchor.MiddleRight, font);
        colsInput = MakeInputField("ColsInput", designT,
            new Vector2(0.40f, baseY), new Vector2(0.40f, baseY), Vector2.zero, 100, 32,
            "10", font); colsInput.text = "10";

        MakeText("LblTypes", designT, "类型数",
            new Vector2(0.55f, baseY), new Vector2(0.55f, baseY), Vector2.zero, 70, 28,
            18, TextAnchor.MiddleRight, font);
        typesInput = MakeInputField("TypesInput", designT,
            new Vector2(0.60f, baseY), new Vector2(0.60f, baseY), Vector2.zero, 100, 32,
            "8", font); typesInput.text = "8";

        MakeText("LblCount", designT, "生成数量",
            new Vector2(0.75f, baseY), new Vector2(0.75f, baseY), Vector2.zero, 80, 28,
            18, TextAnchor.MiddleRight, font);
        genCountInput = MakeInputField("CountInput", designT,
            new Vector2(0.82f, baseY), new Vector2(0.82f, baseY), Vector2.zero, 80, 32,
            "5", font); genCountInput.text = "5";

        // 8 指标设置：每指标一行，包含 标签 / 数值或分级(切换) / 当前所属分级标签
        metricLabels = new Text[8];
        metricInputs = new InputField[8];
        metricGradeDrops = new Dropdown[8];
        metricUseGradeToggles = new Toggle[8];
        string[] labels = DifficultyAnalyzer.MetricLabels;

        // —— 布局 2.0：统一表头在 76% 高，每行 4.5% 步长（8 行共占 36%），
        //    最后一行指标位于 40%，再空 2% 给 DD 预览行 (ddY=34%)，
        //    DD 预览底部约 28%，与底部 3 个按钮 (10% 区顶部) 间距 ≈194 像素，无重叠。
        const float HEAD_Y = 0.76f;
        const float ROW_STEP = 0.045f;

        // 标题行
        MakeText("H1", designT, "指标",
            new Vector2(0.12f, HEAD_Y), new Vector2(0.12f, HEAD_Y), Vector2.zero, 280, 26,
            18, TextAnchor.MiddleCenter, font);
        MakeText("H2", designT, "归一化值 [0,1]",
            new Vector2(0.40f, HEAD_Y), new Vector2(0.40f, HEAD_Y), Vector2.zero, 220, 26,
            18, TextAnchor.MiddleCenter, font);
        MakeText("H3", designT, "或按分级随机",
            new Vector2(0.62f, HEAD_Y), new Vector2(0.62f, HEAD_Y), Vector2.zero, 220, 26,
            18, TextAnchor.MiddleCenter, font);
        MakeText("H4", designT, "当前所属分级",
            new Vector2(0.85f, HEAD_Y), new Vector2(0.85f, HEAD_Y), Vector2.zero, 160, 26,
            18, TextAnchor.MiddleCenter, font);

        for (int i = 0; i < 8; i++)
        {
            float y = HEAD_Y - ROW_STEP * (i + 1);
            metricLabels[i] = MakeText($"Label_{i}", designT, labels[i],
                new Vector2(0.12f, y), new Vector2(0.12f, y), Vector2.zero, 280, 30,
                16, TextAnchor.MiddleCenter, font);

            // 数值输入框（默认模式）
            metricInputs[i] = MakeInputField($"MetricVal_{i}", designT,
                new Vector2(0.40f, y), new Vector2(0.40f, y), Vector2.zero, 160, 30,
                "0.50", font, 16);
            metricInputs[i].text = "0.50";
            metricInputs[i].contentType = InputField.ContentType.DecimalNumber;
            metricInputs[i].characterLimit = 6;

            // 分级下拉框（启用 useGrade 后生效）
            metricGradeDrops[i] = MakeDropdown($"MetricGrade_{i}", designT,
                new Vector2(0.62f, y), new Vector2(0.62f, y), Vector2.zero, 160, 30,
                gradeOptions, font, 16);

            // Toggle：切换使用分级还是数值
            metricUseGradeToggles[i] = MakeToggle($"UseGrade_{i}", designT,
                new Vector2(0.30f, y), new Vector2(0.30f, y), Vector2.zero, 110, 30,
                "按分级", font);

            // 当前所属分级显示（自动更新，只读）
            MakeText($"CurGrade_{i}", designT, "普通",
                new Vector2(0.85f, y), new Vector2(0.85f, y), Vector2.zero, 140, 30,
                16, TextAnchor.MiddleCenter, font);
        }

        // 综合难度 DD 实时预览：位于最后一行指标下 2% 屏，绝对不与最后一行指标重叠。
        float ddY = HEAD_Y - ROW_STEP * 9 - 0.01f;
        MakeText("DD_Label", designT, "综合难度 DD (含所属分级)：",
            new Vector2(0.35f, ddY), new Vector2(0.35f, ddY), Vector2.zero, 320, 32,
            22, TextAnchor.MiddleRight, font);
        ddPreviewText = MakeText("DD_Value", designT, "0.500  (普通)",
            new Vector2(0.58f, ddY), new Vector2(0.58f, ddY), Vector2.zero, 360, 32,
            24, TextAnchor.MiddleLeft, font);

        // 生成关卡按钮（底部偏中）
        generateBtn = MakeButton("GenerateLevelsBtn", designT,
            new Vector2(0.5f, 0.10f), new Vector2(0.5f, 0.10f), Vector2.zero, 240, 50,
            "生成关卡", font);
        generateBtn.GetComponent<Image>().color = new Color(0.9f, 0.55f, 0.15f); // 橙色

        // 生成进度条（生成关卡按钮正下方 18px 处，宽 520 高 26，默认隐藏）
        GameObject pgBg = new GameObject("GenProgressBar");
        pgBg.transform.SetParent(designT, false);
        RectTransform pgRt = pgBg.AddComponent<RectTransform>();
        pgRt.anchorMin = new Vector2(0.5f, 0.10f); pgRt.anchorMax = new Vector2(0.5f, 0.10f);
        pgRt.pivot = new Vector2(0.5f, 1);           // 顶部对齐按钮底部
        pgRt.sizeDelta = new Vector2(520, 26);
        pgRt.anchoredPosition = new Vector2(0, -25 - 18); // 按钮高 50 → 底部在按钮锚点下 25px；再留 18px 间距
        Image pgBgImg = pgBg.AddComponent<Image>();
        pgBgImg.color = new Color(0.05f, 0.05f, 0.10f, 0.9f);
        // 橙色填充条
        GameObject pgFill = new GameObject("Fill");
        pgFill.transform.SetParent(pgBg.transform, false);
        RectTransform pfrt = pgFill.AddComponent<RectTransform>();
        pfrt.anchorMin = Vector2.zero; pfrt.anchorMax = Vector2.one;
        pfrt.offsetMin = Vector2.zero; pfrt.offsetMax = Vector2.zero;
        Image pfi = pgFill.AddComponent<Image>();
        pfi.color = new Color(0.92f, 0.55f, 0.15f);
        pfi.type = Image.Type.Filled;
        pfi.fillMethod = Image.FillMethod.Horizontal;
        pfi.fillOrigin = (int)Image.OriginHorizontal.Left;
        pfi.fillAmount = 0f;
        genProgressFill = pfi;
        // 百分比文字
        GameObject pgLabel = new GameObject("Label");
        pgLabel.transform.SetParent(pgBg.transform, false);
        RectTransform plrt = pgLabel.AddComponent<RectTransform>();
        plrt.anchorMin = Vector2.zero; plrt.anchorMax = Vector2.one;
        plrt.offsetMin = Vector2.zero; plrt.offsetMax = Vector2.zero;
        Text plText = pgLabel.AddComponent<Text>();
        plText.text = "0 / 0   0%";
        plText.font = font;
        plText.fontSize = 16;
        plText.alignment = TextAnchor.MiddleCenter;
        plText.color = Color.white;
        genProgressLabel = plText;
        pgBg.SetActive(false);

        // 通关记录按钮（左下）
        recordsBtn = MakeButton("RecordsBtn", designT,
            new Vector2(0.15f, 0.10f), new Vector2(0.15f, 0.10f), Vector2.zero, 180, 40,
            "通关记录", font);
        recordsBtn.GetComponent<Image>().color = new Color(0.3f, 0.6f, 1f);

        // 返回标题按钮（右下）
        backToTitleBtn2 = MakeButton("DesignBackBtn", designT,
            new Vector2(0.85f, 0.10f), new Vector2(0.85f, 0.10f), Vector2.zero, 180, 40,
            "返回标题", font);
        backToTitleBtn2.GetComponent<Image>().color = new Color(0.5f, 0.5f, 0.55f);

        // ========================== LevelSelectorPanel ==========================
        levelSelectorPanel = new GameObject("LevelSelectorPanel");
        levelSelectorPanel.transform.SetParent(canvasT, false);
        RectTransform sr = levelSelectorPanel.AddComponent<RectTransform>();
        sr.anchorMin = Vector2.zero; sr.anchorMax = Vector2.one;
        sr.sizeDelta = Vector2.zero; sr.anchoredPosition = Vector2.zero;
        Image sBg = levelSelectorPanel.AddComponent<Image>();
        sBg.color = new Color(0.10f, 0.10f, 0.16f);
        levelSelectorPanel.SetActive(false);
        selectorT = levelSelectorPanel.transform;

        MakeText("SelTitle", selectorT, "已生成关卡列表（点击跳转体验）",
            new Vector2(0.5f, 0.94f), new Vector2(0.5f, 0.94f), Vector2.zero, 800, 40,
            28, TextAnchor.MiddleCenter, font);

        // 关卡列表容器：ScrollRect + Content + Mask，动态生成关卡卡片
        GameObject scrollViewObj = new GameObject("LevelScrollView");
        scrollViewObj.transform.SetParent(selectorT, false);
        RectTransform svRect = scrollViewObj.AddComponent<RectTransform>();
        svRect.anchorMin = new Vector2(0.1f, 0.15f);
        svRect.anchorMax = new Vector2(0.9f, 0.88f);
        svRect.sizeDelta = Vector2.zero; svRect.anchoredPosition = Vector2.zero;
        Image svBg = scrollViewObj.AddComponent<Image>();
        svBg.color = new Color(0.08f, 0.08f, 0.12f, 0.8f);
        ScrollRect scrollRect = scrollViewObj.AddComponent<ScrollRect>();
        scrollViewObj.AddComponent<RectMask2D>();

        // Viewport（滚动遮罩层）
        GameObject vpObj = new GameObject("Viewport");
        vpObj.transform.SetParent(scrollViewObj.transform, false);
        RectTransform vpRect = vpObj.AddComponent<RectTransform>();
        vpRect.anchorMin = Vector2.zero; vpRect.anchorMax = Vector2.one;
        vpRect.offsetMin = new Vector2(10, 10); vpRect.offsetMax = new Vector2(-10, -10);
        vpObj.AddComponent<RectMask2D>();
        scrollRect.viewport = vpRect;

        // Content（关卡卡片的父容器）
        levelListContainer = new GameObject("LevelListContent");
        levelListContainer.transform.SetParent(vpObj.transform, false);
        RectTransform llRect = levelListContainer.AddComponent<RectTransform>();
        llRect.anchorMin = new Vector2(0, 1); llRect.anchorMax = new Vector2(1, 1);
        llRect.pivot = new Vector2(0.5f, 1);
        llRect.sizeDelta = new Vector2(0, 0); // 动态高度
        llRect.anchoredPosition = Vector2.zero;
        scrollRect.content = llRect;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;

        // 刷新列表按钮
        refreshListBtn = MakeButton("RefreshListBtn", selectorT,
            new Vector2(0.15f, 0.07f), new Vector2(0.15f, 0.07f), Vector2.zero, 160, 40,
            "刷新列表", font);

        // 返回关卡设计按钮
        backToDesignBtn = MakeButton("BackToDesignBtn", selectorT,
            new Vector2(0.5f, 0.07f), new Vector2(0.5f, 0.07f), Vector2.zero, 200, 40,
            "返回关卡设计", font);

        // 返回标题按钮
        backToTitleBtn3 = MakeButton("SelBackTitleBtn", selectorT,
            new Vector2(0.85f, 0.07f), new Vector2(0.85f, 0.07f), Vector2.zero, 160, 40,
            "返回标题", font);

        // ========================== RecordPanel（通关记录） ==========================
        recordPanel = new GameObject("RecordPanel");
        recordPanel.transform.SetParent(canvasT, false);
        RectTransform rr = recordPanel.AddComponent<RectTransform>();
        rr.anchorMin = Vector2.zero; rr.anchorMax = Vector2.one;
        rr.sizeDelta = Vector2.zero; rr.anchoredPosition = Vector2.zero;
        Image rBg = recordPanel.AddComponent<Image>();
        rBg.color = new Color(0.06f, 0.06f, 0.12f, 0.96f);
        recordPanel.SetActive(false);
        Transform rT = recordPanel.transform;

        MakeText("RecTitle", rT, "通关时间记录（可删除）",
            new Vector2(0.5f, 0.94f), new Vector2(0.5f, 0.94f), Vector2.zero, 600, 40,
            28, TextAnchor.MiddleCenter, font);

        currentRecordLevelLabel = MakeText("RecCurLevel", rT, "当前关卡：-",
            new Vector2(0.5f, 0.88f), new Vector2(0.5f, 0.88f), Vector2.zero, 1000, 30,
            18, TextAnchor.MiddleCenter, font);

        // 记录列表 ScrollView
        GameObject recSV = new GameObject("RecScrollView");
        recSV.transform.SetParent(rT, false);
        RectTransform recSVR = recSV.AddComponent<RectTransform>();
        recSVR.anchorMin = new Vector2(0.15f, 0.20f);
        recSVR.anchorMax = new Vector2(0.85f, 0.82f);
        recSVR.sizeDelta = Vector2.zero; recSVR.anchoredPosition = Vector2.zero;
        Image recBg = recSV.AddComponent<Image>();
        recBg.color = new Color(0.08f, 0.08f, 0.12f, 0.9f);
        ScrollRect recScroll = recSV.AddComponent<ScrollRect>();
        recSV.AddComponent<RectMask2D>();

        GameObject recVp = new GameObject("Viewport");
        recVp.transform.SetParent(recSV.transform, false);
        RectTransform recVpR = recVp.AddComponent<RectTransform>();
        recVpR.anchorMin = Vector2.zero; recVpR.anchorMax = Vector2.one;
        recVpR.offsetMin = new Vector2(10, 10); recVpR.offsetMax = new Vector2(-10, -10);
        recVp.AddComponent<RectMask2D>();
        recScroll.viewport = recVpR;

        recordsListContainer = new GameObject("RecordContent");
        recordsListContainer.transform.SetParent(recVp.transform, false);
        RectTransform rcRect = recordsListContainer.AddComponent<RectTransform>();
        rcRect.anchorMin = new Vector2(0, 1); rcRect.anchorMax = new Vector2(1, 1);
        rcRect.pivot = new Vector2(0.5f, 1);
        rcRect.sizeDelta = new Vector2(0, 0);
        rcRect.anchoredPosition = Vector2.zero;
        recScroll.content = rcRect;
        recScroll.horizontal = false; recScroll.vertical = true;
        recScroll.movementType = ScrollRect.MovementType.Clamped;

        // 删除单条记录按钮
        recordDeleteBtn = MakeButton("RecordDeleteBtn", rT,
            new Vector2(0.25f, 0.10f), new Vector2(0.25f, 0.10f), Vector2.zero, 200, 44,
            "删除选中记录", font);
        recordDeleteBtn.GetComponent<Image>().color = new Color(0.7f, 0.3f, 0.3f);

        recordBackBtn = MakeButton("RecordBackBtn", rT,
            new Vector2(0.50f, 0.10f), new Vector2(0.50f, 0.10f), Vector2.zero, 200, 44,
            "返回关卡设计", font);

        recordsCloseBtn = MakeButton("RecordsCloseBtn", rT,
            new Vector2(0.75f, 0.10f), new Vector2(0.75f, 0.10f), Vector2.zero, 200, 44,
            "关闭", font);
        recordsCloseBtn.GetComponent<Image>().color = new Color(0.5f, 0.5f, 0.55f);
    }
}
