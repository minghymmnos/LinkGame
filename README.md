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
- **结束游戏**（红色按钮）— 关闭程序（`Application.Quit()`）

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
所有图标消除后弹出，显示：
- **恭喜过关！**
- **用时 MM:SS**
- **再来一局** — 重置游戏
- **结束游戏** — 关闭程序

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

- `SetupGame()` — 依次创建：Camera → Canvas → Tile预制体 → GridManager → LineDrawer → UIManager（含UI） → GameController → BotController；然后调用 `SetReferences` 将 GridManager、LineDrawer 和 UIManager 注入 GameController
- `SetupUI()` — 创建四个 UI 层次：
  1. **TitlePanel**：标题文字 + 开始/退出按钮
  2. **GameHud**（游戏时可见，标题时隐藏）：分数/计时文字 + 难度指标按钮 + 重新开始/重新排列/Bot 演示按钮 + GameOverPanel + DifficultyPanel
  3. **GameOverPanel**（默认隐藏）：过关提示文字 + 再来一局/结束游戏按钮
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
│   └── TitleQuitButton ("结束游戏")
│
└── GameHud (默认隐藏，游戏时可见)
    ├── ScoreText (左上，0%~30% 宽度)
    ├── TimerText (右上，70%~100% 宽度)
    ├── DifficultyButton (左侧偏上，紫色，anchoredPosition=(15,-80))
    ├── DifficultyPanel (左侧居中，默认隐藏)
    │   ├── RectMask2D 遮罩
    │   └── DifficultyText (18 行指标，内边距 16px)
    ├── RestartButton (底部左)
    ├── ShuffleButton (底部中)
    ├── BotButton (底部上)
    └── GameOverPanel (semi-transparent overlay, 默认隐藏)
        ├── GameOverText (用时信息)
        ├── OverRestartButton ("再来一局")
        └── OverQuitButton ("结束游戏")
```

**关键函数**：

- `SetUIReferences()` — 类似于依赖注入，GameInitializer 创建完所有 UI 对象后调用此方法传递引用。同时在此绑定所有按钮的点击事件（包括 difficultyButton.onClick → OnDifficultyClicked）
- `ShowTitle(bool)` — 控制标题面板和游戏 HUD 的可见性切换（两者的可见性始终相反）
- `OnStartClicked()` — 隐藏标题，调用 `GameController.StartNewGame()`
- `OnRestartClicked()` — 先停止 Bot（如果有运行），再调用 StartNewGame
- `OnBotClicked()` — 调用 BotController.ToggleBot() 后立即更新按钮文字
- `OnDifficultyClicked()` — 难度指标按钮回调：
  1. 切换 difficultyPanel 的显隐状态
  2. 若为显示状态，调用 `DifficultyAnalyzer.Analyze()` 计算当前关卡难度指标
  3. 调用 `UpdateDifficultyText(DifficultyAnalyzer.FormatMetrics(...))` 格式化并显示文本

---

## 四、数据流和调用链

### 游戏启动流程
```
GameInitializer.Start()
  └─ SetupGame()
       ├─ 创建 Camera、Canvas
       ├─ 创建 GridManager → 设置参数
       ├─ 创建 LineDrawer → 设置预制体
       ├─ 创建 UIManager → SetupUI() 创建所有 UI
       ├─ 创建 GameController → SetReferences(gm, ld, ui)
       └─ 创建 BotController
```

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
            ├─ DifficultyAnalyzer.Analyze()
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
