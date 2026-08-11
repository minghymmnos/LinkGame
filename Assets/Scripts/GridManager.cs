using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 网格管理器。
/// 负责游戏棋盘的数据维护和瓦片管理，包括：
/// - 网格数据的初始化和重置
/// - 瓦片对象的实例化和销毁
/// - 瓦片的增删改查（消除、剩余检测、有效移动检测）
/// - 剩余瓦片的重新排列
/// - 网格坐标与世界坐标的转换
/// 采用单例模式。
/// </summary>
public class GridManager : MonoBehaviour
{
    // ---------- 网格参数 ----------
    private int rows = 8;                      // 网格行数
    private int cols = 10;                     // 网格列数
    private float tileSize = 0.9f;             // 每个瓦片的世界空间边长
    private Vector2 gridCenter = Vector2.zero; // 网格中心点（世界坐标）

    // ---------- 瓦片资源 ----------
    private GameObject tilePrefab;             // 瓦片预制体
    private Transform tileContainer;           // 瓦片父容器，方便统一管理
    private Color[] tileColors;                // 图标颜色数组
    private Sprite[] tileSprites;              // 可选精灵图片数组（为空时使用纯色）

    // ---------- 网格数据 ----------
    // int? 类型：null 表示空格（已消除或边界外），非 null 表示该格子有瓦片（值为类型 ID）
    // 数组尺寸为 (rows+2)×(cols+2)，索引 0 和 rows+1 / cols+1 是边界外虚拟区域，始终为 null
    // 这样设计使路径可以绕到棋盘外部
    private int?[,] gridData;
    // Tile 组件引用数组，与 gridData 一一对应
    private Tile[,] tiles;

    // ---------- 单例和随机数 ----------
    private static GridManager instance;
    private System.Random rng = new System.Random(); // 随机数生成器，用于洗牌

    /// <summary>网格行数（只读）</summary>
    public int Rows => rows;
    /// <summary>网格列数（只读）</summary>
    public int Cols => cols;
    /// <summary>网格数据（只读），供 PathFinder 和 BotController 访问</summary>
    public int?[,] GridData => gridData;
    /// <summary>瓦片组件数组（只读），供 BotController 访问</summary>
    public Tile[,] Tiles => tiles;
    /// <summary>单例访问器</summary>
    public static GridManager Instance => instance;

    private void Awake()
    {
        instance = this; // 设置单例
    }

    /// <summary>设置网格参数（行数、列数、瓦片大小、中心点）</summary>
    public void SetGridSettings(int r, int c, float size, Vector2 center)
    {
        rows = r;
        cols = c;
        tileSize = size;
        gridCenter = center;
    }

    /// <summary>设置瓦片预制体和容器</summary>
    public void SetTilePrefabAndContainer(GameObject prefab, Transform container)
    {
        tilePrefab = prefab;
        tileContainer = container;
    }

    /// <summary>设置图标颜色数组</summary>
    public void SetTileColors(Color[] colors)
    {
        tileColors = colors;
    }

    /// <summary>设置可选精灵图片数组</summary>
    public void SetTileSprites(Sprite[] sprites)
    {
        tileSprites = sprites;
    }

    /// <summary>
    /// 初始化/重置网格。
    /// 生成新的配对数据，实例化所有瓦片，并设置边界为空。
    /// </summary>
    public void InitializeGrid()
    {
        InitializeGrid(null, -1, -1, -1, null);
    }

    /// <summary>
    /// 扩展初始化：允许临时覆盖 rows/cols/typeCount，或直接指定类型列表。
    /// </summary>
    /// <param name="overrideRows">-1 表示使用默认值</param>
    /// <param name="overrideCols">-1 表示使用默认值</param>
    /// <param name="overrideTypeCount">-1 表示使用默认值（颜色/精灵数）</param>
    /// <param name="exactTypeList">
    /// 若不为空，则长度必须 == rows*cols，按行优先顺序直接分配类型 ID
    /// （索引 0 = (row=1,col=1)，索引 cols-1 = (row=1,col=cols)，以此类推）。
    /// </param>
    public void InitializeGrid(int?[,] snapshot,
        int overrideRows = -1, int overrideCols = -1, int overrideTypeCount = -1,
        List<int> exactTypeList = null)
    {
        // 应用覆盖参数
        int useRows = overrideRows > 0 ? overrideRows : rows;
        int useCols = overrideCols > 0 ? overrideCols : cols;
        rows = useRows; cols = useCols;

        // 创建数据数组，尺寸加 2 是为了包含边界外虚拟区域
        gridData = new int?[rows + 2, cols + 2];
        tiles = new Tile[rows + 2, cols + 2];

        int totalTiles = rows * cols;
        int totalPairs = totalTiles / 2; // 总对数（若 rows*cols 奇，这里向下取整）

        // 判断是否使用精灵图片
        bool useSprites = tileSprites != null && tileSprites.Length > 0;
        int availableTypes = useSprites ? tileSprites.Length :
            Mathf.Max(1, tileColors != null ? tileColors.Length : 8);

        // 实际类型数：优先 exactTypeList 里出现的种类数，其次 overrideTypeCount，最后默认
        int numTypes;
        if (overrideTypeCount > 0) numTypes = Mathf.Min(overrideTypeCount, availableTypes);
        else numTypes = availableTypes;
        numTypes = Mathf.Max(1, numTypes);

        // 生成类型列表
        List<int> typeList;
        if (exactTypeList != null && exactTypeList.Count == totalTiles)
        {
            typeList = new List<int>(exactTypeList);
        }
        else if (snapshot != null && snapshot.GetLength(0) == rows + 2 && snapshot.GetLength(1) == cols + 2)
        {
            // 从快照展开为行优先列表
            typeList = new List<int>(totalTiles);
            for (int r = 1; r <= rows; r++)
                for (int c = 1; c <= cols; c++)
                    typeList.Add(snapshot[r, c] ?? 0);
        }
        else
        {
            // 默认生成：循环使用类型，两两配对后打乱
            typeList = new List<int>();
            for (int i = 0; i < totalPairs; i++)
            {
                int type = i % numTypes;
                typeList.Add(type);
                typeList.Add(type);
            }
            // 若网格总数为奇数（理论不应出现），补一个占位类型
            while (typeList.Count < totalTiles) typeList.Add(0);
            Shuffle(typeList);
        }

        // ===== 关键修复：typeId 归一化，保证与可用颜色/精灵数匹配 =====
        // 当玩家在关卡设计器设置的 typeCount > tileColors/tileSprites 数量时，
        // 快照里会出现 typeId 超过调色板容量的情况：如果直接传给 Tile.Init，
        // 相同颜色的两个瓦片 typeId 会不同（例如 typeId=8 与 0 颜色相同），
        // 玩家看起来颜色一致但永远无法消除，直观表现就是"生成的关卡玩不了"。
        // 解决方案：对 typeList 中每一项统一对 availableTypes 取模，
        // 保证"相同颜色的瓦片必然有相同 typeId 所以必然可配对"。
        // 由于 LevelGenerator 生成时每个 id 都有偶数次出现，取模后仍然保持偶次。
        int mod = Mathf.Max(1, availableTypes);
        for (int i = 0; i < typeList.Count; i++)
        {
            int raw = typeList[i];
            // 负数保险：处理 -1
            if (raw < 0) raw = 0;
            typeList[i] = raw % mod;
        }
        // 同步修正 overrideTypeCount：取模后实际最大可能类型数 = Mathf.Min(override, available)
        if (overrideTypeCount > 0)
            numTypes = Mathf.Min(numTypes, availableTypes);

        // 清除旧的瓦片对象
        ClearExistingTiles();

        int index = 0;
        Vector3 startPos = CalculateStartPosition(); // 计算左上角起始坐标

        // 逐行逐列实例化瓦片（实际数据范围 1~rows、1~cols）
        for (int r = 1; r <= rows; r++)
        {
            for (int c = 1; c <= cols; c++)
            {
                int typeId = typeList[index];
                gridData[r, c] = typeId;

                Vector3 pos = startPos + new Vector3((c - 1) * tileSize, -(r - 1) * tileSize, 0);
                GameObject tileObj = Instantiate(tilePrefab, pos, Quaternion.identity, tileContainer);
                tileObj.transform.localScale = Vector3.one * tileSize;
                tileObj.SetActive(true);

                Tile tile = tileObj.GetComponent<Tile>();
                if (tile == null) tile = tileObj.AddComponent<Tile>();

                Color color = tileColors[typeId % Mathf.Max(1, tileColors.Length)];
                Sprite sprite = useSprites ? tileSprites[typeId % Mathf.Max(1, tileSprites.Length)] : null;
                tile.Init(r, c, typeId, color, OnTileClicked, sprite);

                tiles[r, c] = tile;
                index++;
            }
        }

        // 将边界外区域（索引 0 和 rows+1 / cols+1）设为 null，表示可通行
        for (int r = 0; r <= rows + 1; r++)
        {
            gridData[r, 0] = null;
            gridData[r, cols + 1] = null;
        }
        for (int c = 0; c <= cols + 1; c++)
        {
            gridData[0, c] = null;
            gridData[rows + 1, c] = null;
        }
    }

    /// <summary>
    /// 将当前 gridData 压缩成 int 列表（-1 = null），长度 = (rows+2)*(cols+2)。
    /// 用于 LevelInstance 持久化。
    /// </summary>
    public List<int> SnapshotGrid()
    {
        if (gridData == null) return new List<int>();
        int R = gridData.GetLength(0);
        int C = gridData.GetLength(1);
        List<int> snap = new List<int>(R * C);
        for (int r = 0; r < R; r++)
            for (int c = 0; c < C; c++)
                snap.Add(gridData[r, c] ?? -1);
        return snap;
    }

    /// <summary>从 SnapshotGrid 压缩列表恢复 int?[,] 网格数据（仅返回，不赋值）。</summary>
    public static int?[,] RestoreSnapshot(List<int> snap, int rows, int cols)
    {
        if (snap == null || snap.Count != (rows + 2) * (cols + 2)) return null;
        int?[,] g = new int?[rows + 2, cols + 2];
        int k = 0;
        for (int r = 0; r < rows + 2; r++)
            for (int c = 0; c < cols + 2; c++)
            {
                int v = snap[k++];
                g[r, c] = v < 0 ? (int?)null : v;
            }
        return g;
    }

    /// <summary>
    /// 清除容器中所有已存在的瓦片对象。
    /// 使用 DestroyImmediate 因为可能在同一帧内重新生成。
    /// </summary>
    private void ClearExistingTiles()
    {
        if (tileContainer == null)
            return;

        // 从后往前删除，避免索引错乱
        for (int i = tileContainer.childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(tileContainer.GetChild(i).gameObject);
        }
    }

    /// <summary>
    /// 计算网格左上角的起始世界坐标。
    /// 使整个网格以 gridCenter 为中心居中。
    /// </summary>
    /// <returns>左上角瓦片中心的世界坐标</returns>
    private Vector3 CalculateStartPosition()
    {
        float totalWidth = (cols - 1) * tileSize;  // 网格总宽度
        float totalHeight = (rows - 1) * tileSize; // 网格总高度
        return new Vector3(
            gridCenter.x - totalWidth / 2,  // 水平居中
            gridCenter.y + totalHeight / 2, // 垂直居中（左上角在上方）
            0
        );
    }

    /// <summary>
    /// Fisher-Yates 洗牌算法。
    /// 随机打乱列表中元素的顺序。
    /// </summary>
    /// <param name="list">待打乱的列表</param>
    private void Shuffle(List<int> list)
    {
        int n = list.Count;
        while (n > 1)
        {
            n--;
            int k = rng.Next(n + 1); // 随机选取一个位置
            // 交换 list[n] 和 list[k]
            int value = list[k];
            list[k] = list[n];
            list[n] = value;
        }
    }

    /// <summary>
    /// 瓦片点击回调。
    /// 由 Tile.OnMouseDown 触发，转发给 GameController 处理。
    /// </summary>
    /// <param name="tile">被点击的瓦片</param>
    private void OnTileClicked(Tile tile)
    {
        GameController.Instance?.OnTileSelected(tile);
    }

    /// <summary>
    /// 移除指定位置的瓦片（消除）。
    /// 将网格数据设为 null，并标记瓦片为空（透明、禁用碰撞器）。
    /// </summary>
    /// <param name="row">行索引</param>
    /// <param name="col">列索引</param>
    public void RemoveTile(int row, int col)
    {
        gridData[row, col] = null; // 数据置空
        if (tiles[row, col] != null)
            tiles[row, col].SetEmpty(); // 瓦片视觉置空
    }

    /// <summary>
    /// 检查网格中是否还有剩余瓦片。
    /// </summary>
    /// <returns>有剩余返回 true，否则 false</returns>
    public bool HasRemainingTiles()
    {
        for (int r = 1; r <= rows; r++)
            for (int c = 1; c <= cols; c++)
                if (gridData[r, c] != null)
                    return true;
        return false;
    }

    /// <summary>
    /// 检查网格中是否存在至少一对可消除的瓦片。
    /// 遍历所有同类型瓦片的两两组合，调用 PathFinder 检测连通性。
    /// </summary>
    /// <returns>有可消除对返回 true，否则 false</returns>
    public bool HasValidMoves()
    {
        // 收集所有剩余瓦片的位置
        List<PathFinder.Point> allTiles = new List<PathFinder.Point>();
        for (int r = 1; r <= rows; r++)
            for (int c = 1; c <= cols; c++)
                if (gridData[r, c] != null)
                    allTiles.Add(new PathFinder.Point(r, c));

        // 遍历所有两两组合
        for (int i = 0; i < allTiles.Count; i++)
        {
            for (int j = i + 1; j < allTiles.Count; j++)
            {
                // 只检查同类型的组合
                if (gridData[allTiles[i].Row, allTiles[i].Col] ==
                    gridData[allTiles[j].Row, allTiles[j].Col])
                {
                    List<PathFinder.Point> path = PathFinder.FindPath(
                        gridData, rows, cols, allTiles[i], allTiles[j]);
                    if (path != null)
                        return true; // 找到一对可连通的
                }
            }
        }
        return false; // 无可消除对
    }

    /// <summary>
    /// 静态方法：将网格坐标转换为世界坐标。
    /// 供 LineDrawer 绘制连线和外部使用。
    /// </summary>
    /// <param name="row">行索引</param>
    /// <param name="col">列索引</param>
    /// <returns>对应的世界坐标</returns>
    public static Vector3 GridToWorldPosition(int row, int col)
    {
        if (instance == null)
            return Vector3.zero;
        return instance.CalculateGridPosition(row, col);
    }

    /// <summary>
    /// 实例方法：网格坐标转世界坐标。
    /// </summary>
    private Vector3 CalculateGridPosition(int row, int col)
    {
        Vector3 startPos = CalculateStartPosition();
        return startPos + new Vector3((col - 1) * tileSize, -(row - 1) * tileSize, 0);
    }

    /// <summary>
    /// 重新排列剩余瓦片。
    /// 收集所有剩余瓦片的类型和位置，打乱类型后重新分配给位置，刷新瓦片外观。
    /// 用于无有效移动时或玩家手动触发。
    /// </summary>
    public void ShuffleRemainingTiles()
    {
        List<int> remainingTypes = new List<int>(); // 剩余瓦片的类型列表
        List<Vector2Int> positions = new List<Vector2Int>(); // 剩余瓦片的位置列表

        // 收集所有剩余瓦片
        for (int r = 1; r <= rows; r++)
        {
            for (int c = 1; c <= cols; c++)
            {
                if (gridData[r, c] != null)
                {
                    remainingTypes.Add(gridData[r, c].Value);
                    positions.Add(new Vector2Int(r, c));
                }
            }
        }

        // 打乱类型顺序
        Shuffle(remainingTypes);

        bool useSprites = tileSprites != null && tileSprites.Length > 0;

        // 将打乱后的类型重新分配到各位置，刷新瓦片外观
        for (int i = 0; i < positions.Count; i++)
        {
            int r = positions[i].x;
            int c = positions[i].y;
            gridData[r, c] = remainingTypes[i]; // 更新数据
            Color color = tileColors[remainingTypes[i] % tileColors.Length];
            Sprite sprite = useSprites ? tileSprites[remainingTypes[i] % tileSprites.Length] : null;
            tiles[r, c].Init(r, c, remainingTypes[i], color, OnTileClicked, sprite); // 刷新瓦片
        }
    }
}
