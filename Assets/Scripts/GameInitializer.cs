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

        // ---------- 7. 创建 UI 管理器并搭建界面 ----------
        GameObject uiObj = new GameObject("UIManager");
        UIManager uiManager = uiObj.AddComponent<UIManager>();
        SetupUI(uiManager, canvasObj, defaultFont); // 创建所有 UI 元素并绑定到 UIManager

        // ---------- 8. 创建游戏控制器并注入依赖 ----------
        GameObject gameObj = new GameObject("GameController");
        GameController gameController = gameObj.AddComponent<GameController>();
        // 将网格管理器、连线绘制器、UI 管理器注入到游戏控制器
        gameController.SetReferences(gridManager, lineDrawer, uiManager);

        // ---------- 9. 创建 Bot 控制器 ----------
        GameObject botObj = new GameObject("BotController");
        botObj.AddComponent<BotController>();

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

        // 分数文字：左上角，锚定左侧 0%~30% 宽度
        Text scoreText = MakeText("ScoreText", hudT, "分数: 0",
            new Vector2(0, 1), new Vector2(0.3f, 1), new Vector2(10, -20), 0, 50,
            28, TextAnchor.MiddleLeft, font);

        // 计时文字：右上角，锚定右侧 70%~100% 宽度
        Text timerText = MakeText("TimerText", hudT, "时间: 00:00",
            new Vector2(0.7f, 1), new Vector2(1, 1), new Vector2(-10, -20), 0, 50,
            24, TextAnchor.MiddleRight, font);

        // "重新开始" 按钮：底部偏左
        Button restartBtn = MakeButton("RestartButton", hudT,
            new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-80, 25), 140, 40,
            "重新开始", font);

        // "重新排列" 按钮：底部偏右
        Button shuffleBtn = MakeButton("ShuffleButton", hudT,
            new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(80, 25), 140, 40,
            "重新排列", font);

        // "Bot 演示" 按钮：底部中间上方
        Button botBtn = MakeButton("BotButton", hudT,
            new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 80), 140, 40,
            "Bot 演示", font);
        botBtn.GetComponent<Image>().color = new Color(0.2f, 0.7f, 0.3f); // 绿色
        Text botBtnText = botBtn.GetComponentInChildren<Text>(); // 保留按钮文字引用，用于切换显示

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

        // "结束游戏" 按钮，位于再来一局按钮下方
        Button overQuitBtn = MakeButton("OverQuitButton", panelObj.transform,
            new Vector2(0.5f, 0.4f), new Vector2(0.5f, 0.4f), new Vector2(0, -70), 160, 50,
            "结束游戏", font);
        overQuitBtn.GetComponent<Image>().color = new Color(0.7f, 0.3f, 0.3f); // 红色

        // ---------- 难度指标面板 ----------
        // 半透明深色面板，显示 8 个指标和综合难度，默认隐藏。
        // 布局说明（参考分辨率 1920×1080）：
        //   - 锚定左侧垂直居中 (0, 0.5)，pivot 左中 (0, 0.5)，从左边缘向右展开
        //   - 尺寸 380×560，覆盖屏幕 y∈[260, 820]
        //   - 顶部留出空间给分数文字(y≈1035)和难度按钮(y≈964~1000)，间距≥140
        //   - 底部留出空间给功能按钮(y≈5~100)，间距≥160
        //   - 右侧 x=395 远离游戏网格(x≥578)，避免遮挡棋盘
        GameObject diffPanel = new GameObject("DifficultyPanel");
        diffPanel.transform.SetParent(hudT, false);
        RectTransform diffRect = diffPanel.AddComponent<RectTransform>();
        diffRect.anchorMin = new Vector2(0, 0.5f);   // 锚定左侧垂直居中
        diffRect.anchorMax = new Vector2(0, 0.5f);
        diffRect.pivot = new Vector2(0, 0.5f);          // 从左边缘中心展开
        diffRect.anchoredPosition = new Vector2(15, 0);
        diffRect.sizeDelta = new Vector2(380, 560);
        Image diffBg = diffPanel.AddComponent<Image>();
        diffBg.color = new Color(0.1f, 0.1f, 0.2f, 0.92f); // 深色半透明背景
        // 添加矩形遮罩，确保文字始终被裁剪在深色区域内，绝不溢出
        diffPanel.AddComponent<RectMask2D>();
        diffPanel.SetActive(false); // 默认隐藏

        // 难度指标文字：完全填充面板内部，留 16px 内边距，保证所有信息落在深色区域内
        Text diffText = MakeText("DifficultyText", diffPanel.transform, "",
            new Vector2(0, 0), new Vector2(1, 1), Vector2.zero, 0, 0,
            14, TextAnchor.UpperLeft, font);
        RectTransform diffTextRect = diffText.GetComponent<RectTransform>();
        diffTextRect.pivot = new Vector2(0.5f, 0.5f);
        diffTextRect.offsetMin = new Vector2(16, 16);     // 左下内边距
        diffTextRect.offsetMax = new Vector2(-16, -16);    // 右上内边距
        diffText.horizontalOverflow = HorizontalWrapMode.Wrap;   // 自动换行，防止横向溢出
        diffText.verticalOverflow = VerticalWrapMode.Truncate;   // 纵向超长时裁剪（由 RectMask2D 兜底）
        diffText.lineSpacing = 1.15f;
        // 文字有效区域：宽 348px、高 528px，可容纳全部 18 行指标文本

        // ---------- 难度指标开关按钮 ----------
        // 位于游戏界面左侧偏上（分数文字下方），pivot 设为左上角便于定位
        // 位置 (15, -80)：顶部 y=1000，底部 y=964，与分数文字(y≥1035)间距 35
        // 与下方难度面板(y≤820)间距 144，确保不发生重叠
        Button diffBtn = MakeButton("DifficultyButton", hudT,
            new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, 130, 36,
            "难度指标", font);
        RectTransform diffBtnRect = diffBtn.GetComponent<RectTransform>();
        diffBtnRect.pivot = new Vector2(0, 1); // 左上角对齐锚点
        diffBtnRect.anchoredPosition = new Vector2(15, -80);
        diffBtn.GetComponent<Image>().color = new Color(0.5f, 0.3f, 0.8f); // 紫色

        // 将所有 UI 引用传递给 UIManager，并绑定按钮事件
        uiManager.SetUIReferences(scoreText, timerText, panelObj, overText,
            restartBtn, shuffleBtn, overRestartBtn, botBtn, botBtnText,
            titlePanel, startBtn, titleQuitBtn, overQuitBtn, gameHud,
            diffPanel, diffText, diffBtn);
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
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        Text text = textObj.AddComponent<Text>();
        text.text = buttonText;
        if (font != null) text.font = font;
        text.fontSize = 20;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleCenter;

        return button;
    }
}
