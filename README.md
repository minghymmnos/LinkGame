# 连连看游戏 — 完整说明文档

## 一、游戏概述

**连连看** 是一款经典的配对消除游戏。游戏在一个 N×M 的网格上放置成对的图标，玩家需要找到两个相同且路径可连通的图标进行消除。全部消除即获胜。

- 网格大小：8 行 × 10 列（共 40 对）读取脚本设定，当非偶数情况时使用默认设置
- 图标种类：8 种（由颜色或精灵图片表示）
- 计时方式：开始游戏后自动计时，过关时显示总用时

---

## 二、游戏功能说明

### 1. 标题界面
程序启动后显示标题界面，包含：
- **游戏标题** "连 连 看"（居中大号字体）
- **开始游戏**（绿色按钮）— 进入游戏，初始化棋盘并开始计时
- **关卡设计**（橙色按钮）— 进入关卡设计器（见第六章）
- **结束游戏**（红色按钮）— 关闭程序（`Application.Quit()`，仅在打包成可执行程序后生效）

### 2. 游戏主界面

| 元素 | 位置 | 说明 |
|------|------|------|
| 分数 | 左上角 | 每消除一对加 10 分 |
| 计时 | 右上角 | 格式 `MM:SS`，从 00:00 开始累加 |
| 难度指标按钮 | 左侧偏上（分数下方） | 紫色按钮，默认隐藏难度面板，点击切换显示 |
| 难度指标面板 | 左侧居中（默认隐藏） | 深色半透明区域，显示 8 个量化难度指标 + 综合难度评分 |
| 重新开始 | 底部左侧 | 重置棋盘、分数和计时，开始新一局 |
| 重新排列 | 底部中间 | 将剩余图标随机打乱位置 |
| Bot 演示 | 底部上方 | 启动/停止自动游戏模式 |
| 返回按钮 | 右上角（计时器下方） | 智能切换：「从关卡列表进入」显示「返回关卡列表」→ 点击回到生成关卡列表页；「从标题页进入」显示「返回标题」→ 点击回到标题页。点击时终止当前局（停止计时、停 Bot、清状态） |

### 2.5 难度指标评价功能

游戏内置完整的关卡难度量化评价体系（基于《连连看关卡生成设计方案》文档），通过点击"难度指标"按钮可查看当前关卡的详细难度评分：

- **综合难度评分 DD**：取值 [0, 1]，并映射为 5 个难度等级（极易 / 简单 / 普通 / 困难 / 极难）
- **视觉感知难度 VPD**：包含 3 个指标
  - M1 有效解密度 VMD：初始可连通配对数 / 理论配对总数（越低越难）
  - M2 图标类型熵 TTE：类型分布的香农熵（越高越难）
  - M3 同类型空间离散度 TSD：同类型瓦片平均曼哈顿距离（越高越难）
- **路径推理难度 PRD**（权重最高，占比 45%）：包含 2 个指标
  - M4 平均路径转弯数 APT：可连通配对路径转弯数平均值（越高越难）
  - M5 复杂路径占比 CPR：需 2 拐角路径的配对比例（越高越难）
- **策略规划难度 SPD**：包含 3 个指标（基于蒙特卡洛 100 次模拟游戏过程）
  - M6 决策宽度 DW：每步可选有效配对数平均值（U 型曲线，两端难、中间易）
  - M7 死锁频率 DF：模拟中无有效移动的发生频率（越高越难）
  - M8 解序列分支度 SB：消除顺序自由度（U 型曲线，两端难、中间易）

### 3. 游戏核心机制

**配对规则**
- 两个图标必须**类型相同**（typeId 相等）
- 两个图标之间必须存在**可通行路径**
- 路径的定义：从一个图标中心到另一个图标中心，**最多经过 2 个拐角**（即 0、1、2 个拐角，对应路径节点数为 2、3、4）

**路径连通规则**
- 路径只能穿过**空格**（已消除的格子）
- 路径可以利用网格**外部的虚拟区域**（gridData 的 0 行/列和 rows+1 行/cols+1 列），这些区域视为空格
- 路径的起止点（两个图标所在格子）即使有图标也视为可通行

**消除流程**
1. 玩家点击第一个图标 → 图标高亮
2. 玩家点击第二个图标 → 检测路径
3. 路径存在 → 画连线 → 消除两个图标 → 加 10 分 → 隐藏高亮
4. 路径不存在 → 取消两个高亮 → 回到待选状态
5. 点击已高亮的图标 → 取消选中

### 4. 自动补位机制
- 当所有图标消除完毕 → 显示过关面板
- 当剩余图标 **没有任何可消除的对**（`HasValidMoves()` 返回 false）→ 自动重新排列

### 5. Bot 演示模式
点击 "Bot 演示" 按钮启动自动游戏：
- Bot 会扫描所有剩余图标，找出**最优配对**并自动点击消除
- 最优配对评分：`score = 拐点数 × 100 + 曼哈顿距离`，分值越低越优先
- 消除间隔约 0.55 秒（可观察过程）
- 再次点击 "停止 Bot" 按钮可随时中断
- 游戏胜利时 Bot 自动停止

### 6. 过关面板
所有图标消除后弹出，**根据本局进入来源显示不同的按钮组合**：

| 进入来源 | 按钮组合 | 说明 |
|----------|----------|------|
| 标题页「开始游戏」 | 上方「再来一局」（默认蓝）<br>下方「返回标题」（蓝灰） | 重开同尺寸新局 / 终止当前局回到标题页 |
| 关卡列表「开始体验」 | **仅一个**「返回关卡列表」（橙色） | 终止当前局 → 回到生成关卡列表页（自动刷新，显示刚保存的通关记录与最新最佳用时） |

> 原「结束游戏」（退出程序）按钮已从过关面板移除，避免玩家误以为通关后要关闭游戏；退出程序仅保留在标题页红色「结束游戏」按钮。

---

## 三、脚本结构和内容

### GameInitializer.cs（游戏初始化器）

**职责**：程序入口，负责创建所有游戏对象和组件，建立组件间的引用关系。

**结构**：
```
GameInitializer
├── 序列化字段（Inspector 可调参数）
│   ├── gridRows/gridCols      — 网格行数/列数（默认 8×10）
│   ├── tileSize               — 瓦片大小（默认 0.85）
│   ├── tileColors[]           — 图标颜色数组（8 种颜色）
│   └── tileSprites[]          — 可选精灵图片数组
│
├── Start()                    — 入口，校验参数后调用 SetupGame()
├── SetupGame()                — 创建所有游戏对象
├── CreateCanvas()             — 创建 UI 画布和事件系统
├── CreateTilePrefab()         — 创建瓦片预设
├── CreateDefaultSprite()      — 生成纯色精灵纹理
├── CreateLineRendererPrefab() — 创建连线预设
├── SetupUI()                  — 创建标题界面、游戏 HUD、过关面板、难度指标面板
├── MakeText()                 — 通用文字创建方法
└── MakeButton()               — 通用按钮创建方法
```

**关键函数**：

- `SetupGame()` — 依次创建：Camera → Canvas → Tile预制体 → GridManager → LineDrawer → **GameController** → UIManager（含UI） → BotController；其中 GameController 必须先于 UIManager 创建（保证 UIManager 订阅通关事件时单例已存在，否则成绩记录不写入），随后调用 `SetReferences` 将 GridManager、LineDrawer 和 UIManager 注入 GameController
- `SetupUI()` — 创建四个 UI 层次：
  1. **TitlePanel**：标题文字 + 开始/关卡设计/退出按钮
  2. **GameHud**（游戏时可见，标题时隐藏）：分数/计时文字 + 返回按钮 + 难度指标按钮 + 重新开始/重新排列/Bot 演示按钮 + GameOverPanel + DifficultyPanel
  3. **GameOverPanel**（默认隐藏）：过关提示文字 + 来源感知按钮（再来一局/返回关卡列表/返回标题）
  4. **DifficultyPanel**（默认隐藏）：深色半透明面板 + RectMask2D 遮罩 + 多行指标文本
- `MakeText()` — 创建 Text 组件，设置锚点、位置、字体、大小、溢出模式
- `MakeButton()` — 创建 Button 组件（含 Image+Button+子 Text），返回 Button 引用

**难度指标 UI 布局**（参考分辨率 1920×1080）：
```
难度指标按钮 (DifficultyButton)
├── 锚点: (0,1)~(0,1)，pivot=(0,1)，anchoredPosition=(15, -80)
├── 尺寸: 130×36
├── 位置: 左上角（分数文字下方），y∈[964, 1000]
└── 颜色: 紫色 (0.5, 0.3, 0.8)

难度指标面板 (DifficultyPanel)
├── 锚点: (0, 0.5)~(0, 0.5)，pivot=(0, 0.5)，anchoredPosition=(15, 0)
├── 尺寸: 380×560，覆盖 y∈[260, 820]
├── 背景: 深蓝灰色半透明 (0.1,0.1,0.2,0.92)
├── 遮罩: RectMask2D（确保文字不溢出深色区域）
└── 文字内边距: 16px 四周（有效文本区域 348×528）
    ├── 自动换行 (Wrap)
    └── 超长时纵向裁剪 (Truncate) + RectMask2D 双重保护
布局间距（确保不与其他元素重叠）：
  - 与分数文字 (y≥1035) 间距 ≥ 35
  - 难度按钮底 (y=964) 与面板顶 (y=820) 间距 = 144
  - 面板底 (y=260) 与底部功能按钮 (y≤100) 间距 = 160
  - 面板右边缘 x=395，远离游戏网格 (x≥578)，不遮挡棋盘
```

---

### GameController.cs（游戏控制器）

**职责**：管理游戏的核心状态流转和消除逻辑。

**结构**：
```
GameState 枚举          — Idle / Selected / Animating / Won
GameController
├── 私有字段
│   ├── gridManager      — 网格管理器引用
│   ├── lineDrawer       — 连线绘制器引用
│   ├── uiManager        — UI 管理器引用
│   ├── currentState     — 当前游戏状态
│   ├── firstSelectedTile— 第一个选中的瓦片
│   ├── score            — 当前分数
│   ├── pairsRemaining   — 剩余待消除对数
│   ├── gameTime         — 游戏运行时间
│   └── isTimerRunning   — 计时器是否运行
│
├── Awake()              — 设置单例
├── Start()              — 空（由标题按钮触发开始）
├── Update()             — 计时更新
├── SetReferences()      — 注入依赖
├── StartNewGame()       — 开始/重置游戏
├── OnTileSelected()     — 瓦片点击处理
├── ClearSelectedTile()  — 清空选中状态
└── TryMatch()           — 配对检测和消除
```

**状态机**：
```
Idle ──点击瓦片──→ Selected ──点击同瓦片──→ Idle
                      ↓
                   点击另一瓦片
                      ↓
                  Animating ──消除完成──→ Idle
                              ──全部消除──→ Won
```

**关键函数**：

- `StartNewGame()` — 重置所有状态和数据（分数、计时、选中状态），调用 `GridManager.InitializeGrid()` 重新生成棋盘，隐藏过关面板，启动计时器
- `OnTileSelected(Tile)` — 根据当前状态处理瓦片点击：
  - **Idle**：选中第一个瓦片，进入 Selected
  - **Selected**：如果点击同一个瓦片则取消选中；否则选中第二个并调用 TryMatch
  - **Animating/Won**：忽略点击
- `TryMatch(tile1, tile2)` — 调用 `PathFinder.FindPath()` 检测连通性：
  - **可连通**：画连线 → 回调中消除瓦片 → 更新分数 → 检测是否胜利/需重新排列 → 回到 Idle
  - **不可连通**：取消高亮 → 回到 Idle
- `Update()` — 计时器运行时每帧累加 gameTime 并刷新 UI 显示

---

### GridManager.cs（网格管理器）

**职责**：管理网格数据的初始化和维护，提供瓦片的 CRUD 操作。

**结构**：
```
GridManager
├── 私有字段
│   ├── rows/cols/tileSize/gridCenter — 网格参数
│   ├── tilePrefab/tileContainer      — 瓦片预设和容器
│   ├── tileColors/tileSprites        — 图标外观
│   ├── gridData[,]                   — 网格数据（int? 类型，null 表示空格）
│   ├── tiles[,]                      — Tile 组件引用数组
│   └── rng                           — 随机数生成器
│
├── Awake()                    — 设置单例
├── SetGridSettings()          — 设置网格参数
├── SetTilePrefabAndContainer()— 设置瓦片预设和容器
├── SetTileColors()            — 设置颜色数组
├── SetTileSprites()           — 设置精灵数组
├── InitializeGrid()           — 初始化/重置网格
├── ClearExistingTiles()       — 清除旧瓦片
├── CalculateStartPosition()   — 计算网格起始坐标
├── Shuffle(List)              — Fisher-Yates 洗牌算法
├── OnTileClicked()            — 瓦片点击回调转发
├── RemoveTile()               — 移除指定位置的瓦片
├── HasRemainingTiles()        — 检查是否还有剩余瓦片
├── HasValidMoves()            — 检查是否有可消除对
├── GridToWorldPosition()      — 静态方法：网格坐标转世界坐标
├── CalculateGridPosition()    — 网格坐标转世界坐标（实例方法）
└── ShuffleRemainingTiles()    — 打乱剩余瓦片位置
```

**数据结构**：
- `gridData` — `int?[rows+2, cols+2]`，索引范围 0~rows+1，0 和 rows+1、0 和 cols+1 是外部虚拟区域（始终为 null），实际数据在 1~rows、1~cols
- `tiles` — `Tile[rows+2, cols+2]`，与 gridData 一一对应

**关键函数**：

- `InitializeGrid()` — 生成新的配对数据：
  1. 计算总对数 `(rows×cols)/2`
  2. 生成类型列表（每种类型出现两次），Fisher-Yates 打乱
  3. 清除旧的 Tile 对象
  4. 计算起始位置，逐行逐列实例化 Tile
  5. 设置 Tile 的行列、类型、颜色、精灵和点击回调
  6. 将网格边界外（索引 0 和 rows+1、0 和 cols+1）设为 null
- `HasValidMoves()` — 遍历所有剩余瓦片的同类型组合，对每组调用 PathFinder.FindPath，只要存在一条路径即返回 true
- `ShuffleRemainingTiles()` — 收集所有剩余瓦片的类型和位置，类型列表打乱后重新赋值给位置，刷新 Tile 外观
- `GridToWorldPosition(row, col)` — 静态方法，供 LineDrawer 将路径点转换为世界坐标以绘制连线

---

### PathFinder.cs（路径查找器）

**职责**：核心算法，检测两个瓦片之间是否存在可连通的路径。

**结构**：
```
PathFinder
├── Point 结构体
│   ├── Row/Col — 行列坐标
│   └── 构造函数
│
├── FindPath()         — 主入口，按 0→1→2 拐角顺序尝试
├── IsCellPassable()   — 判断格子是否可通行
├── TryDirectConnect() — 检测直线连接（0 拐角）
├── TryOneTurnConnect()— 检测 1 拐角连接
└── TryTwoTurnConnect()— 检测 2 拐角连接
```

**算法详解**：

`FindPath()` 按复杂度递增的顺序尝试三种连接方式：

1. **直线连接（0 拐角）** — `TryDirectConnect()`
   - 同行或同列时，检查中间所有格子是否为空
   - 路径 = `[起点, 终点]`（2 个点）

2. **1 拐角连接** — `TryOneTurnConnect()`
   - 构造两个拐点：(start.Row, end.Col) 和 (end.Row, start.Col)
   - 对每个拐点，检查拐点本身是否可通行，以及起点→拐点、拐点→终点两条直线段是否通畅
   - 路径 = `[起点, 拐点, 终点]`（3 个点）

3. **2 拐角连接** — `TryTwoTurnConnect()`
   - **水平扫描**：遍历每一行 r（0~rows+1），构造拐点 (r, start.Col) 和 (r, end.Col)，检查三个直线段
   - **垂直扫描**：遍历每一列 c（0~cols+1），构造拐点 (start.Row, c) 和 (end.Row, c)，检查三个直线段
   - 路径 = `[起点, 拐点1, 拐点2, 终点]`（4 个点）

**关键特性**：
- 扫描范围包含 0 和 rows+1 / 0 和 cols+1 这些虚拟边界，使得路径可以绕到网格外部
- `IsCellPassable()` 对起点和终点格子特殊处理（始终返回 true），对其他格子检查 `grid[row, col] == null`

---

### Tile.cs（瓦片）

**职责**：代表棋盘上的单个瓦片，管理外观和点击响应。

**结构**：
```
Tile
├── 公开属性
│   ├── Row/Col      — 网格坐标（只读）
│   ├── TypeId       — 图标类型 ID（只读）
│   └── IsEmpty      — 是否已消除
│
├── 私有字段
│   ├── spriteRenderer    — 主精灵渲染器
│   ├── highlightRenderer — 高亮精灵渲染器
│   └── onClickCallback   — 点击回调
│
├── Awake()               — 获取组件引用
├── Init()                — 初始化瓦片（2 个重载）
├── SetEmpty()            — 标记为已消除
├── SetHighlight()        — 设置高亮
├── FitSpriteToTile()     — 自适应精灵大小
├── SetRenderers()        — 设置渲染器引用
└── OnMouseDown()         — 点击事件
```

**关键函数**：

- `Init(row, col, typeId, color, onClick, sprite)` — 设置行列、类型、颜色/精灵，激活碰撞器。如果传入了 sprite 则使用精灵并调用 `FitSpriteToTile()`，否则使用纯色
- `SetEmpty()` — 将颜色设为透明、隐藏高亮、禁用碰撞器
- `SetHighlight(bool)` — 显示/隐藏高亮 Sprite（黄色半透明叠加层）
- `FitSpriteToTile()` — 计算精灵的 PPU 比例，缩放使精灵适配瓦片大小
- `OnMouseDown()` — Unity 消息，非空格子且回调存在时触发点击

---

### BotController.cs（Bot 控制器）

**职责**：实现自动游戏功能，自动寻找最优配对并执行消除。

**结构**：
```
BotController
├── isRunning          — 是否正在运行
├── botCoroutine       — 协程引用
│
├── Awake()            — 设置单例
├── ToggleBot()        — 切换启停
├── StartBot()         — 启动 Bot
├── StopBot()          — 停止 Bot（清除选中、更新 UI）
├── BotLoop()          — 主循环协程
├── FindBestPair()     — 寻找最优配对
└── UpdateUI()         — 更新按钮文字
```

**Bot 算法**：

`BotLoop()` 协程的工作循环：
1. 等待游戏处于 Idle 状态（或 Won 状态）
2. 如果游戏已胜利 → 停止 Bot
3. 调用 `FindBestPair()` 寻找最优配对
4. 找不到配对 → 调用 `ShuffleRemainingTiles()` 打乱后继续
5. 找到配对 → 通过 `GameController.OnTileSelected()` 依次点击两个瓦片
6. 每次点击间隔 0.15 秒，消除后等待 0.4 秒，让玩家看到过程

`FindBestPair()` 的配对选择策略：
1. 扫描网格，按类型（typeId）分组所有剩余瓦片
2. 对每组内的所有两两组合，调用 `PathFinder.FindPath()` 检测连通性
3. 对可连通的配对计算评分：`score = 拐点数 × 100 + 曼哈顿距离`
4. 拐点越少越好（0 < 1 < 2），同拐角时距离越近越好
5. 返回评分最低的配对

---

### LineDrawer.cs（连线绘制器）

**职责**：在两个匹配的瓦片之间绘制金色连线，并在短暂显示后自动清除。

**结构**：
```
LineDrawer
├── linePrefab        — 连线预制体
├── lineWidth         — 线宽（0.08）
├── showDuration      — 显示时长（0.3 秒）
├── activeLines[]     — 当前激活的连线列表
│
├── SetLinePrefab()       — 设置连线预制体
├── DrawPath()            — 绘制路径
├── ClearLineAfterDelay() — 延迟清除协程
└── ClearAllLines()       — 清除所有连线
```

**关键函数**：

- `DrawPath(path, onComplete)` — 根据 PathFinder 返回的点列表，实例化 LineRenderer，设置每个点的世界坐标（通过 `GridManager.GridToWorldPosition()` 转换），加入激活列表，启动延迟清除协程
- `ClearLineAfterDelay(line, onComplete)` — 等待 `showDuration`（0.3 秒）后销毁 LineRenderer 对象，调用 `onComplete` 回调（即 GameController 的消除逻辑）

---

### DifficultyAnalyzer.cs（难度分析器）

**职责**：计算连连看关卡的 8 个量化难度指标和综合难度评分，是实现"可量化难度评价"的核心组件。基于《连连看关卡生成设计方案》文档定义的评价体系。这是一个静态工具类，无需挂载到 GameObject 上。

**结构**：
```
DifficultyMetrics 嵌套类
├── 8 个原始指标 + 归一化值
│   ├── VMD / VMD_norm    — M1 有效解密度
│   ├── TTE / TTE_norm    — M2 图标类型熵
│   ├── TSD / TSD_norm    — M3 同类型空间离散度
│   ├── APT / APT_norm    — M4 平均路径转弯数
│   ├── CPR / CPR_norm    — M5 复杂路径占比
│   ├── DW  / DW_norm     — M6 决策宽度（U 型归一化）
│   ├── DF  / DF_norm     — M7 死锁频率
│   └── logSB / SB_norm   — M8 解序列分支度对数（U 型归一化）
│
├── 维度得分和综合难度
│   ├── VPD              — 视觉感知难度 [0,1]
│   ├── PRD              — 路径推理难度 [0,1]
│   ├── SPD              — 策略规划难度 [0,1]
│   ├── DD               — 综合难度评分 [0,1]
│   └── difficultyLevel  — 难度等级文字（极易/简单/普通/困难/极难）
│
└── 中间计算数据
    ├── totalPairs / validPairCount / numTypes / totalTiles
    └── （供 UI 显示统计信息）

DifficultyAnalyzer（静态类）
├── 归一化权重常量
│   ├── w1~w8                — 各维度内指标权重
│   ├── alpha / beta / gamma — 三维度聚合权重（VPD=30%, PRD=45%, SPD=25%）
│   ├── DW_LOW/DW_MID        — DW U 型曲线分界点
│   ├── SB_LOW/SB_MID        — SB U 型曲线分界点
│   └── MONTE_CARLO_RUNS=100 — 蒙特卡洛模拟次数
│
├── Analyze()                          — 主入口：计算完整难度指标
├── CalculateTypeEntropy()             — M2：计算类型分布香农熵
├── CalculateSpatialDispersion()       — M3：计算同类型瓦片空间离散度
├── SimulateGameplay()                 — M6/M7/M8：蒙特卡洛模拟完整游戏
├── FindAllValidPairs()                — 模拟辅助：查找当前所有可连通配对
├── HasRemainingTilesInSim()           — 模拟辅助：检查模拟网格是否有剩余瓦片
├── ShuffleSimGrid()                   — 模拟辅助：模拟重排操作
├── CopyGrid()                         — 模拟辅助：复制网格数据
├── NormalizeUShape()                  — U 型归一化函数（DW 和 SB 使用）
├── GetDifficultyLevel()               — 将 DD 映射为文字等级
└── FormatMetrics()                    — 将指标格式化为 UI 显示的多行文本
```

**8 个指标计算原理**：

1. **M1 有效解密度 (VMD)** = 初始可连通配对数 / 理论配对总数
   - 理论配对总数 = Σ C(n_i, 2)（按类型组内组合数之和）
   - 可连通配对 = 遍历所有同类型两两组合调用 PathFinder.FindPath() 统计
   - 归一化：`VMD_norm = 1 - VMD`（值越低越难）

2. **M2 图标类型熵 (TTE)** = -Σ p_i × log₂(p_i)（香农熵，衡量类型分布不均匀程度）
   - 归一化：除以最大熵 log₂(K)，K 为类型数

3. **M3 同类型空间离散度 (TSD)** = 各类型瓦片间平均曼哈顿距离的跨类型平均值
   - 归一化：线性映射 [1, R+C-2] → [0, 1]

4. **M4 平均路径转弯数 (APT)** = 所有可连通配对路径转弯数之和 / 可连通配对数
   - 归一化：`APT / 2`（最大转弯数为 2）

5. **M5 复杂路径占比 (CPR)** = 需要 2 拐角的可连通配对数 / 可连通配对总数
   - 归一化：已是 [0, 1] 比例值

6. **M6 决策宽度 (DW)** = 模拟中每步可选有效配对数的平均值
   - 归一化：**U 型函数**，DW≤2 时归一=1（选择太少），2<DW≤8 线性降为 0，>8 线性上升到 1（选择太多）

7. **M7 死锁频率 (DF)** = 模拟中死锁（有剩余但无可消除对）发生次数 / 总步数
   - 归一化：已是 [0, 1] 比例值

8. **M8 解序列分支度 (SB)** = Σ log₁₀(第i步可选配对数)，再取模拟平均
   - 归一化：**U 型函数**，分界点 logSB≤5 为 1，5<logSB≤40 线性降为 0，>40 线性上升

**维度聚合公式**：
```
VPD = 0.40·VMD_norm + 0.30·TTE_norm + 0.30·TSD_norm
PRD = 0.60·APT_norm + 0.40·CPR_norm
SPD = 0.40·DW_norm  + 0.30·DF_norm  + 0.30·SB_norm
DD  = 0.30·VPD + 0.45·PRD + 0.25·SPD （综合难度，Clamp 到 [0,1]）
```

**蒙特卡洛模拟 (SimulateGameplay)**：
为计算 M6/M7/M8，需要模拟完整的消除过程 100 次取平均：
1. 复制 gridData 到独立模拟网格，避免修改原数据
2. 循环直到网格清空：
   - 查找所有可连通配对（vpc）
   - vpc=0 且有剩余 → 死锁计数+1，调用 ShuffleSimGrid 模拟重排
   - vpc=0 且无剩余 → 通关，退出
   - 累加 DW += vpc、logSB += log₁₀(vpc)
   - 随机选一对消除（用 Random.Range 而非最优策略，更接近真实玩家行为）
3. 100 次模拟的平均值即为最终 DW、DF、logSB 值

**关键函数**：

- `Analyze()` — 完整指标计算流程：收集类型分组 → 遍历所有同类型组合算路径 → 计算 M1~M5 → SimulateGameplay 算 M6~M8 → 归一化 → 三维度聚合 → 输出综合难度 DD
- `NormalizeUShape(value, low, mid, high)` — U 型归一化：低于 low 返回 1，[low,mid] 线性降到 0，超过 mid 线性升到 1
- `FormatMetrics(metrics)` — 格式化输出 18 行文本，包含综合难度 + 三维度得分 + 8 个指标（原始值和归一化值） + 统计信息

---

### UIManager.cs（UI 管理器）

**职责**：管理所有 UI 元素的显示和交互回调。

**结构**：
```
UIManager
├── 私有字段（所有 UI 组件的引用）
│   ├── scoreText/timerText           — 分数和计时
│   ├── gameOverPanel/gameOverText    — 过关面板
│   ├── restart/shuffle/bot/overRestart — 各按钮
│   ├── botButtonText                 — Bot 按钮文字
│   ├── titlePanel/startButton/titleQuitButton — 标题界面
│   ├── overQuitButton                — 过关退出按钮
│   ├── gameHud                       — 游戏界面容器
│   ├── difficultyPanel               — 难度指标面板（左侧居中深色区）
│   ├── difficultyText                — 难度指标多行文字
│   └── difficultyButton              — 难度指标开关按钮（紫色）
│
├── Awake()                   — 设置单例
├── SetUIReferences()         — 接收所有 UI 引用并绑定回调
├── ShowTitle()               — 切换标题/游戏界面
├── UpdateScore()             — 更新分数显示
├── UpdateTimer()             — 更新计时显示（MM:SS 格式）
├── ShowGameOver()            — 显示/隐藏过关面板
├── UpdateBotButtonText()     — 更新 Bot 按钮文字
├── ShowDifficultyPanel()     — 显示/隐藏难度指标面板
├── UpdateDifficultyText()    — 更新难度指标文字内容
├── OnStartClicked()          — 开始游戏
├── OnRestartClicked()        — 重新开始
├── OnShuffleClicked()        — 重新排列
├── OnBotClicked()            — 切换 Bot
├── OnDifficultyClicked()     — 难度指标按钮：切换面板 + 计算指标
└── OnQuitClicked()           — 退出程序
```

**UI 层次结构**：
```
Canvas (ScreenSpaceOverlay, 1920×1080 参考)
├── TitlePanel (深色背景)
│   ├── TitleText ("连 连 看")
│   ├── StartButton ("开始游戏")
│   ├── LevelDesignButton ("关卡设计"，橙色)
│   └── TitleQuitButton ("结束游戏")
│
└── GameHud (默认隐藏，游戏时可见)
    ├── ScoreText (左上，0%~30% 宽度)
    ├── TimerText (右上，70%~100% 宽度)
    ├── BackButton (右上，计时器下方，来源感知：「返回关卡列表 / 返回标题」)
    ├── DifficultyButton (左侧偏上，紫色，anchoredPosition=(15,-80))
    ├── DifficultyPanel (左侧居中，默认隐藏)
    │   ├── RectMask2D 遮罩
    │   └── DifficultyText (18 行指标，内边距 16px)
    ├── RestartButton (底部左)
    ├── ShuffleButton (底部中)
    ├── BotButton (底部上)
    └── GameOverPanel (semi-transparent overlay, 默认隐藏，来源感知)
        ├── GameOverText (用时信息)
        ├── OverRestartButton ("再来一局"，仅标题来源显示)
        └── OverQuitButton (来源感知：「返回关卡列表」橙色 / 「返回标题」蓝灰)
```

> UIManager 还管理关卡设计器三块动态面板（LevelDesignPanel / LevelSelectorPanel / RecordPanel），详见第六章 6.1 界面结构。

**关键函数**：

- `SetUIReferences()` — 类似于依赖注入，GameInitializer 创建完所有 UI 对象后调用此方法传递引用。同时在此绑定所有按钮的点击事件（包括 difficultyButton.onClick → OnDifficultyClicked）
- `ShowTitle(bool)` — 控制标题面板和游戏 HUD 的可见性切换（两者的可见性始终相反）
- `OnStartClicked()` — 隐藏标题，调用 `GameController.StartNewGame()`
- `OnRestartClicked()` — 先停止 Bot（如果有运行），再调用 StartNewGame
- `OnBotClicked()` — 调用 BotController.ToggleBot() 后立即更新按钮文字
- `OnDifficultyClicked()` — 难度指标按钮回调：
  1. 切换 difficultyPanel 的显隐状态
  2. 若为显示状态：自定义关卡优先复用 `LevelInstance.metrics` 预计算快照，否则调用 `DifficultyAnalyzer.Analyze()` 实时计算当前盘面
  3. 调用 `UpdateDifficultyText(DifficultyAnalyzer.FormatMetrics(...))` 格式化并显示文本
- `EnsureLevelClearedSubscribed()` — 幂等订阅 `GameController.OnLevelCleared`（先 -= 再 +=），在开始游戏 / 再来一局 / 关卡列表开始体验三入口调用，防御初始化时序导致通关成绩不记录
- `CoGenerateLevels()` — 协程逐关生成关卡 + 橙色动态进度条（详见 6.4）
- `OnGameOverBackButtonClicked()` / `OnInGameBackClicked()` — 过关面板与 HUD 返回按钮回调，按 `_lastFromLevelList` 来源分派：返回关卡列表（`OpenLevelSelector`）或返回标题（`ShowTitle`），前置调用 `TerminateCurrentLevel` 终止当前局

---

## 四、数据流和调用链

### 游戏启动流程
```
GameInitializer.Start()
  └─ SetupGame()
       ├─ 创建 Camera、Canvas
       ├─ 创建 GridManager → 设置参数
       ├─ 创建 LineDrawer → 设置预制体
       ├─ 创建 GameController（必须早于 UI！）
       ├─ 创建 UIManager → SetupUI() 创建所有 UI → 内部订阅 GameController.OnLevelCleared
       ├─ gameController.SetReferences(gridManager, lineDrawer, uiManager)（依赖注入）
       └─ 创建 BotController
```

> **创建顺序关键点**：`GameController` 必须先于 `UIManager` 创建。`UIManager.SetUIReferences` 内部会订阅 `GameController.Instance.OnLevelCleared`（用于通关自动写记录）；若顺序相反，订阅时单例尚为 null 会被跳过，通关记录将永远无法写入（曾为此专门修复）。GameController 的依赖注入 `SetReferences` 则统一放在 UI 创建完成之后。

### 用户点击消除流程
```
Tile.OnMouseDown()
  └─ GridManager.OnTileClicked()
       └─ GameController.OnTileSelected(tile)
            ├─ [第一次] 高亮 tile → state = Selected
            └─ [第二次] TryMatch(tile1, tile2)
                 ├─ PathFinder.FindPath()
                 │    ├─ TryDirectConnect()
                 │    ├─ TryOneTurnConnect()
                 │    └─ TryTwoTurnConnect()
                 ├─ [有路径] LineDrawer.DrawPath()
                 │    └─ 延迟 0.3s →
                 │         GridManager.RemoveTile()
                 │         UIManager.UpdateScore()
                 │         [全部消除] UIManager.ShowGameOver()
                 │         [无有效移动] GridManager.ShuffleRemainingTiles()
                 └─ [无路径] 取消高亮，回到 Idle
```

### Bot 工作流程
```
BotController.StartBot()
  └─ BotLoop() 协程
       ├─ WaitUntil(Idle)
       ├─ FindBestPair() → 遍历所有同类型配对 → PathFinder.FindPath() → 评分排序
       ├─ [有配对] OnTileSelected(tile1) → 0.15s → OnTileSelected(tile2) → 0.4s
       ├─ [无配对且有剩余] ShuffleRemainingTiles() → 0.5s
       ├─ [无配对且无剩余] 退出
       └─ [游戏胜利] StopBot()
```

### 难度指标查看流程
```
玩家点击 DifficultyButton
  └─ UIManager.OnDifficultyClicked()
       ├─ 切换 difficultyPanel.activeSelf（显示 ↔ 隐藏）
       │
       └─ [显示状态]
            ├─ [自定义关卡且存在 metrics 快照] 直接复用 LevelInstance.metrics（不重算，保证权威值）
            ├─ [否则] DifficultyAnalyzer.Analyze()
            │    ├─ 收集类型分组 → 计算 totalPairs / validPairCount
            │    ├─ 遍历同类型组合 → PathFinder.FindPath()
            │    │    └─ 汇总 M1 VMD / M2 TTE / M3 TSD / M4 APT / M5 CPR
            │    ├─ SimulateGameplay()（蒙特卡洛 100 次模拟）
            │    │    ├─ CopyGrid() 复制独立网格
            │    │    ├─ FindAllValidPairs() → 统计 vpc
            │    │    ├─ 累加 DW / DF / logSB
            │    │    └─ [死锁] ShuffleSimGrid() 模拟重排
            │    ├─ 8 个指标归一化（M6/M8 用 U 型归一化）
            │    ├─ 三维度聚合 (VPD/PRD/SPD) → 综合难度 DD
            │    └─ GetDifficultyLevel(DD) → 输出难度等级
            │
            ├─ DifficultyAnalyzer.FormatMetrics(metrics)
            │    └─ 格式化 18 行文本（综合难度 + 三维度 + 8 指标 + 统计信息）
            │
            └─ UIManager.UpdateDifficultyText(text)
                 └─ difficultyText.text = text（显示在 DifficultyPanel 内）
```

---

## 五、关键技术点

### 网格寻路算法
使用**有限拐角路径搜索**：
- 网格数据结构为 `(rows+2)×(cols+2)`，外圈作为虚拟通道，使路径可以绕到棋盘外部
- 搜索策略：先尝试最简单的情况（直线），逐步增加复杂度（1 拐角、2 拐角）
- 这是一种贪心式而非搜索式的算法，效率高、实现简单

### 状态机设计
GameController 使用四状态状态机（Idle → Selected → Animating → Won）管理游戏流程，防止在动画播放或游戏结束时的非法操作。

### UI 层次管理
通过 `TitlePanel`（标题）和 `GameHud`（游戏 HUD）两个容器的可见性交替切换，实现场景快速切换而无需销毁/创建对象。GameOverPanel 嵌套在 GameHud 内，作为模态覆盖层。

### 单例模式
GameController、GridManager、UIManager、BotController 均使用单例模式，方便跨组件访问。Awake() 中设置 `instance = this`。

### 协程驱动
- LineDrawer 使用协程实现延迟清除连线
- BotController 使用协程实现自动游戏循环，`WaitUntil` 等待条件满足，`WaitForSeconds` 控制操作间隔

### 适配不同分辨率
Canvas 使用 `ScaleWithScreenSize` 模式，参考分辨率 1920×1080，UI 元素使用**百分比锚定**（0~0.3、0.7~1）适应不同宽高比。
针对 **4K 显示器**（3840×2160 或非 16:9 窗口）做了专项优化：
- `CanvasScaler.screenMatchMode = MatchWidthOrHeight` + `matchWidthOrHeight = 0.5`（宽高各 50% 权重），使 UI 在任意分辨率下按整数倍缩放，避免非整数 scaleFactor 导致文字双线性插值发虚
- 主相机 `clearFlags = SolidColor` + `backgroundColor = Color.black` 纯黑清屏，避免 Skybox 模式下瓦片边缘出现 1px 白边/毛边

### 可量化难度评价体系
基于《连连看关卡生成设计方案》文档实现三维度 × 8 指标的完整难度量化模型：
- **三维度加权聚合**：视觉感知 VPD (30%) + 路径推理 PRD (45%) + 策略规划 SPD (25%)
- **8 指标归一化策略**：线性归一化 (M1~M5) + U 型曲线归一化 (M6/M8，两端难中间易) + 比例值直接使用 (CPR/DF)
- **综合难度 DD 映射**：[0, 0.2) 极易 / [0.2, 0.4) 简单 / [0.4, 0.6) 普通 / [0.6, 0.8) 困难 / [0.8, 1.0] 极难
- 指标含义覆盖玩家从**视觉感知**（看棋盘找配对有多难）、**路径推理**（找到配对后能否想出连接路线）、到**策略规划**（消除顺序对通关难度的长期影响）三个认知阶段，具有充分的合理性

### 蒙特卡洛模拟 (Monte Carlo Simulation)
为计算决策宽度 (DW)、死锁频率 (DF)、解序列分支度 (SB) 三个需要全局消除过程数据的指标，采用蒙特卡洛方法：
- 模拟次数：100 次（平衡精度与性能）
- 消除策略：随机选择可消除对（而非最优策略），更接近真实玩家行为
- 数据隔离：每次模拟复制独立 gridData，绝不修改原棋盘
- 死锁处理：模拟中遇到无有效移动且有剩余瓦片时，调用 ShuffleSimGrid 模拟重排并计入死锁次数
- 性能：单次模拟 ≈ 40 次消除 × 100 次运行，在 8×10 网格上毫秒级完成

### UI 布局精准控制与防重叠
难度指标面板与按钮采用**锚点 + pivot + anchoredPosition** 组合精确定位，基于 1920×1080 参考分辨率核算所有元素边界：
- 难度面板与分数文字、功能按钮、游戏网格的最小间距均 > 140 像素，确保不发生重叠
- 使用 **RectMask2D** 组件为难度面板添加矩形遮罩，无论文字内容多少，超出深色区域的部分一律被裁剪
- 文本采用「Wrap 自动换行 + Truncate 纵向裁剪 + RectMask2D 遮罩」三重保护机制，保证信息始终完整落在深色区域内
- 文字四周内边距 16px，避免文本紧贴面板边缘影响可读性

---

## 六、关卡设计器

在标题界面的「开始游戏」按钮下方新增**橙色「关卡设计」**按钮，玩家可进入可视化关卡设计流程：**配置参数 → 预测综合难度 → 批量生成 N 个关卡 → 选择关卡进入体验 → 通关后记录时间并可查看/删除**。整体流程保证不破坏原有「标题界面 / 游戏 HUD / 难度指标面板」等既有内容与功能。

### 6.1 入口与界面结构

```
标题界面 TitlePanel
  └─ 关卡设计按钮 (橙色)
       └─ LevelDesignPanel (关卡设计面板)
            ├─ 基础参数：行数 / 列数 / 配对类型数 / 生成关卡数量
            ├─ 8 个难度指标设置区（每行：标签 / 按分级 Toggle / 归一化值 / 分级下拉 / 当前所属分级）
            ├─ 综合难度 DD 实时预览 (数值 + 等级)
            ├─ 生成进度条（生成时显示：橙色填充条 + 「X/N  XX%」+ 超时计数）
            ├─ 通关记录按钮 / 返回标题按钮
            └─ 生成关卡按钮 → 进入 LevelSelectorPanel (关卡列表)
                 ├─ ScrollRect + Content，每个关卡一张卡片
                 │    ├─ 关卡编号、id、尺寸、类型数、DD、最佳用时、通关次数
                 │    ├─ 开始体验 → GameHud，手动进入游戏
                 │    └─ 查看记录 → RecordPanel（该关卡的通关记录）
                 ├─ 刷新列表 / 返回关卡设计 / 返回标题
                 └─ 通关 RecordPanel
                      ├─ 表头 4 列（可点击切换排序 + ↑/↓ 指示）：
                      │    通关序号 / 通关时长 / 通关日期 / 操作
                      ├─ 每条记录：第 N 次通关 / mm:ss.cs / yyyy-MM-dd HH:mm:ss / 行内删除按钮
                      ├─ 最佳用时记录以金绿色高亮
                      ├─ 删除选中记录（底部）/ 返回关卡设计 / 关闭
                      └─ 无记录时显示「📭 尚无通关记录」占位提示
```

### 6.2 难度指标的双向联动

对 8 个量化指标 (M1~M8) 同时支持两种设置方式，并保持**双向联动**：

| 操作 | 行为 |
|------|------|
| 玩家在「归一化值」输入框手动输入 0~1 数值 | 右侧「当前所属分级」立即按区间（极易/简单/普通/困难/极难）反查并刷新文字颜色，同时底部综合 DD 实时重算 |
| 玩家勾选「按分级」Toggle 并在下拉选择分级 | 在对应分级区间内**随机生成一个归一化值**，同步写入数值输入框，并刷新综合 DD |
| 切换「按分级」Toggle 开关 | 开启：数值输入框只读，分级下拉可用，立即按当前分级随机一次；关闭：数值输入框可写，下拉只读，当前所属分级按现有值反查 |
| 修改行数 / 列数 / 类型数 | 综合 DD 预览立即按 PredictOverallDD 重新估算并显示颜色化难度等级 |

5 个难度分级对应的归一化区间：

```
极易 VeryEasy : [0.00, 0.20)
简单 Easy     : [0.20, 0.40)
普通 Normal   : [0.40, 0.60)
困难 Hard     : [0.60, 0.80)
极难 VeryHard : [0.80, 1.00]
```

### 6.3 综合难度 DD 的实时预测

综合难度 DD 采用 `DifficultyAnalyzer.PredictOverallDD(rows, cols, types, norms)` 估算：
- 结构项：`结构难度 = clamp((R*C*types) / 2000, 0, 1)`，使棋盘尺寸和类型数对 DD 产生单调贡献（越大越难）
- 指标项：`8 指标加权` 采用 VPD(30%) + PRD(45%) + SPD(25%) 与分析体系一致的权重
- 总项：`0.4 * 结构难度 + 0.6 * 指标加权`，兼顾"尺寸类型大小"和"指标组合难度"

### 6.4 生成关卡

点击「生成关卡」按钮后，`UIManager` 启动**协程逐关生成** `CoGenerateLevels`，并显示**动态进度条**（不阻塞 UI）：

1. **生成前清空旧关卡**：调用 `LevelRecordManager.ClearAll()` 移除之前所有批次的关卡与记录，保证关卡列表**严格等于本次设定的数量 N**（不再跨批次累计）
2. 逐关调用 `LevelGenerator.GenerateSingle(cfg, out timedOut)`，内部采用**两阶段搜索 + 定向调优**（方案3 + 方案2）：
   - 每生成一关 `yield return null` 让 Unity 渲染一帧，进度条实时走一格；每关立即 `UpsertLevel + SaveAll` 落盘（中途崩溃也不丢已生成关卡）
   - **阶段一（快速筛选 + 定向调优）**：只评估静态指标 M1~M5（不跑蒙特卡洛），以**全局随机采样 + 定向变异局部爬山**交替推进——按偏差最大的指标选择变异算子（TTE 偏 → 类型分布迁移；VMD/TSD/APT/CPR 偏 → 位置交换），连续 40 次无改进则从**精英池**（静态最优 top-K=12）换起点或重新全局探索，避免陷入局部最优
   - **阶段二（精评 + 局部精调）**：对精英池跑降采样蒙特卡洛（30 次）按完整 cost（显式指标 + 2×DD 偏差）选出全局最优，再对最优做 8 次"完整评估"的定向变异精调（继续下降动态指标 M6~M8 与 DD 偏差），最后用 100 次全量蒙特卡洛**最终复核**，保证存档指标可信
   - **收敛判据（方案1）**：只要求"玩家显式设置过"的指标落入其目标等级区间即达标；全部未设置（全默认）时保持 8 指标偏差 ≤ 0.18 的旧判据
   - **超时/未达标处理（方案1）**：超过 5 秒未收敛 → 返回**最接近目标的关卡**并在卡片上标注「未完全命中目标指标等级（最大偏差 X）」；仅当连候选都未产出（异常）时才随机生成兜底并标注「生成失败，已按给定参数随机生成」；进度条文字同时显示「超时 N」
3. 全部完成后进度条保持 100% 约 0.8 秒，随后：
   - 全部成功 → 绿色提示并自动跳转关卡列表
   - 有超时 → 黄色警告提示超时数量并跳转关卡列表
   - 生成失败（0 个）→ 红色错误提示，不跳转
4. 关卡结构：`config / gridSnapshot / metrics / overallDD / bestTime / generationTimedOut / remark`，通过 `SaveAll()` 序列化持久化（PlayerPrefs JSON）

### 6.4.1 生成算法详解（两阶段搜索 + 定向调优）

`LevelGenerator.GenerateSingle` 是生成质量的核心，围绕"**尽量命中玩家显式设置的指标等级**"设计，由三部分组成：

**总体流程**

```
GenerateSingle(cfg, out timedOut)
  │  解析目标：ResolveTargetNorms(cfg)  → targetNorms[8]（各指标目标归一值）
  │            PredictOverallDD(...)     → targetDD（目标综合难度）
  │            GetExplicitMetricFlags()  → explicitFlags[8]（哪些指标是玩家显式设置过的）
  │
  ├─ 阶段一：快速筛选 + 定向调优（仅静态指标 M1~M5，不跑蒙特卡洛）
  │    ├─ 探索：GenerateTypeList() 全局随机采样
  │    ├─ 定向爬山：DirectedMutate() 变异 → 静态评估 → cost 不升则接受为新起点
  │    ├─ 多起点：连续 40 次无改进 → 从精英池换起点 / 重新全局探索
  │    └─ 精英池：InsertElite() 保留静态最优 TOP_K=12（按静态 cost 升序）
  │
  ├─ 阶段二：精评 + 局部精调（含蒙特卡洛 M6~M8）
  │    ├─ 对精英池逐个完整评估（SEARCH_MC=30 次模拟）→ 按完整 cost 选全局最优
  │    └─ 对最优再做 8 次定向变异 + 完整评估爬山（继续下降 M6~M8 / DD 偏差）
  │
  └─ 最终复核：对最优以 FULL_MC=100 次模拟精确评估 → 写入 metrics（存档与展示用）
```

**关键数据结构与概念**

| 名称 | 说明 |
|------|------|
| `typeList` | 长度 = rows×cols 的瓦片类型序列（按行优先），`BuildGridForEval` 将其打包成 (rows+2)×(cols+2) 网格求值 |
| `explicitFlags[8]` | 标记"玩家显式设置过"的指标（`useGrade=true` 或归一值偏离默认 0.5）；若全部未设置则退化为全量参与 |
| `cost` | 静态 cost = Σ(显式指标达标偏差) + 0.5×Σ\|实际值−目标值\|（软梯度）；完整 cost 再 + 2×\|DD−目标DD\| |
| `MetricDev` 达标偏差 | 按分级：实际值到目标等级区间的距离（区间内=0）；按精确值：到目标点的绝对偏差 |
| `elite` 精英池 | 保留静态最优 TOP_K 个候选，兼顾「阶段二精评队列」与「爬山换起点来源」 |

**收敛判据（方案1：只约束玩家关心的指标）**

- 玩家显式设置了某指标 → 要求其落入**目标等级区间**（或精确值）才算达标
- 8 个指标全部未设置 → 沿用旧的"8 指标同时偏差 ≤ 0.18"判据
- 静态指标全部达标、且未显式设置动态指标（M6~M8）→ 阶段一即可提前收敛

**变异算子（方案2：指标分治）**

| 算子 | 操作 | 适用场景 |
|------|------|----------|
| `MutateSwap` | 随机交换类型列表两格（等价于交换棋盘两格） | VMD / TSD / APT / CPR 偏差最大时（布局类） |
| `MutateDistribution` | 把"一对"棋子从稀有类型迁移到常见类型（或反向），保持总数且不让类型消失 | **TTE 偏差最大时**（目标熵低→加剧集中，高→趋向均匀） |

`DirectedMutate` 每一步挑出 M1~M5 中偏差最大的指标，据此选择上述算子——这使 **TTE 从"均匀分布下恒为 1、不可调"变为可控指标**。

**关键参数**

| 常量 | 值 | 说明 |
|------|-----|------|
| `TIMEOUT_MS_PER_LEVEL` | 5000 | 单关超时预算（毫秒） |
| `TOP_K` | 12 | 精英池容量（阶段二精评数量） |
| `PHASE1_MAX_ATTEMPTS` | 1500 | 阶段一最大尝试次数（实际受超时预算控制） |
| `STAGNATION_LIMIT` | 40 | 爬山停滞上限（达到后换起点 / 重新探索） |
| `SEARCH_MC` | 30 | 阶段二搜索期蒙特卡洛降采样次数 |
| `PHASE2_HILL_ATTEMPTS` | 8 | 阶段二局部精调的变异尝试次数 |
| `FULL_MC` | 100 | 最终复核的全量蒙特卡洛次数 |
| `ACCEPTABLE_DEVIATION` | 0.18 | 全默认指标下的旧收敛阈值 |

**超时与兜底策略**

| 情况 | 处理 |
|------|------|
| 阶段一超时 | 用已积累的精英池进入阶段二精评 |
| 阶段二超时 | 取已精评候选中的最优（保证至少完成 1 个候选，不会返回 null） |
| 局部精调超时 | 仅停止优化，**不改写** timedOut（避免把已达标关卡误标为超时） |
| 超时/未达标但已产出候选 | 返回**最接近目标**的关卡，`remark` 记录最大偏差，卡片标注「未完全达标」 |
| 完全无产出（异常） | 随机生成兜底并置 `generationTimedOut=true`，卡片标注「生成失败，已按给定参数随机生成」 |

**控制台日志**：每关生成后输出完整指标报告（关卡 id / 目标值+难度等级 → 实际值 → 逐项偏差 → 最大偏差 → 生成状态），达标用普通日志、超时/未达标用黄色警告，便于定位哪些指标难以达成。

### 6.5 关卡体验 + 难度指标实时查看

在关卡列表卡片点击「开始体验」：
- 调用 `GameController.StartNewGame(LevelInstance)`：使用 `gridSnapshot` 恢复棋盘、锁定行数/列数/类型数、绑定当前 `levelId`
- 进入游戏 HUD 后，可随时点击左侧「难度指标」按钮：
  - 若当前关卡是自定义关卡（存在 `LevelInstance.metrics` 快照），优先**直接复用预计算的 8 指标快照**，以保证游戏中任何时刻（包括部分消除后）看到的指标都是该关卡初始完整棋盘的权威值
  - 否则仍按原逻辑实时计算当前盘面
- HUD 右上角「返回按钮」**智能切换文字**（`SyncHudBackButtonText`）：从关卡列表进入显示「返回关卡列表」，点击调用 `GameController.TerminateCurrentLevel` 终止当前局并回到关卡列表；从标题页进入显示「返回标题」，点击回到标题页
- 过关面板按钮组合同样**来源感知**（`SyncGameOverPanelForSource`）：从关卡列表进入时仅显示橙色「返回关卡列表」一个按钮；从标题页进入时显示「再来一局 + 返回标题」
- 通关（所有对消除完毕）时，`GameController` 触发 `OnLevelCleared(levelId, clearTime)`，UIManager 订阅该事件并调用 `LevelRecordManager.AddClearRecord` 写入用时与时间戳

### 6.6 通关记录保存与删除

`LevelRecordManager` 提供 `UpsertLevel / AddClearRecord / DeleteLevel / SaveAll / LoadAll` 全套接口，底层使用 `PlayerPrefs.SetString("LinkGame_Levels", JsonUtility)` 持久化：
- 每个 `LevelInstance` 记录：`id / config / gridSnapshot / metrics / overallDD / bestTime / clearRecords(List<float>) / recordTimestamps(List<string>)`
- 通关后自动保存：`AddClearRecord` 追加一条记录并更新 `bestTime`，随后调用 `SaveAll()`

在 RecordPanel 中查看/删除：
- 可从关卡设计面板「通关记录」按钮或每张关卡卡片「查看记录」进入
- 顶部显示当前关卡元信息（id / 尺寸 / DD / 通关次数）
- **4 列固定宽度布局**（永不重叠）：通关序号(110px) / 通关时长(180px) / 通关日期(260px) / 操作(120px)，列间 10px 间距、行高 44px、行距 8px
- 表头「通关序号 / 通关时长 / 通关日期」为**可点击排序按钮**，当前排序列高亮并带 ↑/↓ 指示：
  - 通关序号：默认升序（第 1 次 → 第 N 次）
  - 通关时长：默认升序（快 → 慢），一键定位最快通关
  - 通关日期：默认降序（最新 → 最旧）
  - 再次点击同列表头翻转升降序
- 每行记录显示：`第 N 次通关 / mm:ss.cs / yyyy-MM-dd HH:mm:ss`（日期精确到秒）
- **最佳用时记录以金绿色高亮**，一眼识别历史最快通关
- 每行右侧带红色「删除」按钮，点击直接删除该条记录（无需先选中）
- 底部「删除选中记录」按钮：点击单条记录行高亮选中后再删除
- 删除通过 `origIndex → 排序映射` 定位真实记录（排序后也不会删错），随后自动重算 bestTime、持久化并刷新列表
- 无任何记录时显示「📭 尚无通关记录」空态占位提示

### 6.7 新增脚本与关键扩展

| 脚本 | 说明 |
|------|------|
| `LevelConfig.cs` | 新增核心数据结构：`DifficultyGrade` 枚举、`DifficultyGradeUtil`（区间 + 随机值 + 颜色/名称映射）、`MetricConstraint`（支持「按值/按分级」两种约束）、`LevelConfig`（行/列/类型数 + 8 个指标约束）、`LevelInstance`（生成后关卡快照 + 指标 + 通关记录） |
| `LevelGenerator.cs` | 静态工具类：`Generate(cfg, count)` 批量生成与 `GenerateSingle(cfg, out timedOut, timeoutMsPerLevel)` 单关生成（供 UI 协程逐关调用），核心为**两阶段搜索 + 定向调优**（方案3 + 方案2）：阶段一静态评估（M1~M5，不跑蒙特卡洛）以「全局随机采样 + 定向变异局部爬山」交替推进（`DirectedMutate` 按偏差最大指标分治：TTE → `MutateDistribution` 类型分布迁移，其余 → `MutateSwap` 位置交换；连续 40 次无改进则从精英池换起点或重启探索）；阶段二对**精英池**（`InsertElite` 维护静态最优 top-K=12）跑降采样蒙特卡洛（SEARCH_MC=30 次）按完整 cost（显式指标达标偏差 + 2×DD 偏差）选优，再对最优做 8 次完整评估的定向变异精调，最后以 FULL_MC=100 次全量模拟**最终复核**。收敛判据（方案1）：仅要求显式设置的指标落入目标等级区间；超时/未达标时**返回最接近目标的关卡**并标注偏差，仅异常时才随机兜底 |
| `DifficultyAnalyzer.cs` | 扩展 `NormalizedValueToGrade` / `GradeToRandomNormalized` 双向转换、`ResolveTargetNorms`（从配置解析 8 个归一化目标值）、`PredictOverallDD`（结构+指标的综合难度估算） |
| `LevelRecordManager.cs` | 单例持久化：`List<LevelInstance>` 以 JSON 存储在 PlayerPrefs，支持关卡增删查（`ClearAll` 生成前清空旧批次保证严格数量）、通关记录添加、删除后重算 bestTime |
| `GameController.cs` | 扩展 `StartNewGame(LevelInstance, int? overrideRows, int? overrideCols)`（仅要求 `level != null` 即绑定 `currentLevelId`，config 为空也不丢成绩）；新增 `OnLevelCleared(string, float)` 事件，`pairsRemaining==0` 时触发；新增 `TerminateCurrentLevel()` 终止当前局 |
| `GridManager.cs` | 扩展 `Initialize(int? overrideRows, int? overrideCols, int? overrideTypes, int? seed, int[] snapshot)`，支持指定尺寸 / 类型数 / 种子 / 快照恢复 |
| `GameInitializer.cs` | 新增 `MakeInputField / MakeDropdown / MakeToggle` 辅助；标题页加入「关卡设计」按钮；新增 `SetupLevelDesignerUI` 构建 LevelDesignPanel / LevelSelectorPanel / RecordPanel 三块 UI 及生成进度条；**GameController 创建顺序提前到 UIManager 之前**（保证通关事件订阅不丢失）；CanvasScaler 宽高匹配 + 相机 SolidColor 黑底（4K 适配） |
| `UIManager.cs` | 重写 `SetUIReferences` 新签名（注入三面板、8 指标控件数组、生成按钮、记录按钮、进度条引用等）；双向联动 / DD 预览 / 生成绑定 / 关卡列表动态卡片 / 通关事件写入 / 记录面板增删均在 UIManager 内实现；`CoGenerateLevels` 协程逐关生成（每关 `yield return null` 刷新橙色进度条 + 严格数量 + 超时计数）；`EnsureLevelClearedSubscribed` 幂等订阅通关事件（开始游戏/再来一局/开始体验三入口防御初始化时序）；记录面板 `RecSortField` 排序模型 + `_recordOrigIndexOrder` 排序映射保证排序后删除不删错 |

### 6.8 完整使用流程

1. 启动游戏 → 标题界面，点击「**关卡设计**」
2. 调整**基础参数**：行数(2~20)、列数(2~20)、类型数(2~64)、生成关卡数量(1~50)
3. 对 8 个**难度指标**逐一设置：
   - 方法 A：在数值输入框直接输入 0~1 数值，观察「当前所属分级」
   - 方法 B：勾选「按分级」，然后在下拉选择极易/简单/普通/困难/极难，值会自动在区间内随机
4. 观察「**综合 DD 预览**」颜色化显示数值与所属分级，必要时回退参数
5. 点击「**生成关卡**」→ 显示**动态进度条**（橙色填充 + 「X/N XX%」+ 超时计数），系统逐关生成**严格等于 N 个**关卡（生成前自动清空旧批次，不跨批次累计；内部两阶段搜索 + 定向调优：静态快速筛选与局部爬山 + 蒙特卡洛精评与精调），每关即时落盘；完成后进度条保持 100% 约 0.8 秒再跳转关卡列表；有超时关卡会以黄色警告提示，卡片标注「未完全命中目标指标等级（最大偏差 X）」
6. 在关卡列表中：
   - 点击某关卡「开始体验」→ 进入游戏 HUD，可随时点击左侧「难度指标」查看该关卡的 8 项难度指标（使用预计算的初始棋盘快照，不受中途消除影响）
   - 点击「查看记录」→ 打开记录面板查看该关卡的过往通关用时
   - 从关卡列表进入的局，HUD 返回按钮与过关面板均只显示「返回关卡列表」入口
7. 通关：恭喜过关弹窗显示用时，同时用时自动保存到记录
8. 查看/排序记录：点击表头「通关序号 / 通关时长 / 通关日期」切换排序（再次点击同列翻转升降序），观察 ↑/↓ 指示；金绿色高亮行为历史最佳用时
9. 删除记录：点击某行高亮 → 底部「删除选中记录」，或直接点行内红色「删除」按钮；删除后自动重算最佳用时并刷新
