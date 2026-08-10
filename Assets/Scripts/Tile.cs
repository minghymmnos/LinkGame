using UnityEngine;

/// <summary>
/// 瓦片类。
/// 代表棋盘上的单个图标瓦片，负责管理瓦片的外观（颜色/精灵/高亮）和鼠标点击响应。
/// 每个瓦片包含两个子精灵：主精灵（MainSprite）和高亮精灵（HighlightSprite）。
/// </summary>
public class Tile : MonoBehaviour
{
    /// <summary>瓦片所在行（网格坐标，只读）</summary>
    public int Row { get; private set; }
    /// <summary>瓦片所在列（网格坐标，只读）</summary>
    public int Col { get; private set; }
    /// <summary>图标类型 ID，相同 ID 的瓦片才能配对（只读）</summary>
    public int TypeId { get; private set; }
    /// <summary>是否已消除（空瓦片）</summary>
    public bool IsEmpty { get; private set; }

    private SpriteRenderer spriteRenderer;      // 主精灵渲染器，显示图标颜色或图片
    private SpriteRenderer highlightRenderer;    // 高亮精灵渲染器，选中时显示黄色高亮
    private Collider2D tileCollider;             // 2D 碰撞器，用于检测鼠标点击
    private System.Action<Tile> onClickCallback; // 点击回调，指向 GridManager.OnTileClicked

    /// <summary>
    /// Unity 生命周期：对象创建时调用。
    /// 通过查找子对象获取渲染器引用（备用方式，实际由 SetRenderers 注入）。
    /// </summary>
    private void Awake()
    {
        spriteRenderer = transform.Find("MainSprite")?.GetComponent<SpriteRenderer>();
        highlightRenderer = transform.Find("HighlightSprite")?.GetComponent<SpriteRenderer>();
        tileCollider = GetComponent<Collider2D>();
    }

    /// <summary>
    /// 初始化瓦片（不使用精灵图片的重载）。
    /// </summary>
    /// <param name="row">行索引</param>
    /// <param name="col">列索引</param>
    /// <param name="typeId">类型 ID</param>
    /// <param name="color">颜色</param>
    /// <param name="onClick">点击回调</param>
    public void Init(int row, int col, int typeId, Color color, System.Action<Tile> onClick)
    {
        Init(row, col, typeId, color, onClick, null);
    }

    /// <summary>
    /// 初始化瓦片（完整版本，可使用精灵图片）。
    /// 设置瓦片的行列、类型、外观和点击回调。
    /// </summary>
    /// <param name="row">行索引</param>
    /// <param name="col">列索引</param>
    /// <param name="typeId">类型 ID</param>
    /// <param name="color">颜色（无精灵时使用）</param>
    /// <param name="onClick">点击回调</param>
    /// <param name="sprite">精灵图片（可空，为空则用纯色）</param>
    public void Init(int row, int col, int typeId, Color color, System.Action<Tile> onClick, Sprite sprite)
    {
        Row = row;
        Col = col;
        TypeId = typeId;
        IsEmpty = false;
        onClickCallback = onClick;

        if (spriteRenderer != null)
        {
            if (sprite != null)
            {
                // 使用精灵图片
                spriteRenderer.sprite = sprite;
                spriteRenderer.color = Color.white; // 精灵显示原色
                FitSpriteToTile(); // 缩放精灵适配瓦片
            }
            else
            {
                // 使用纯色色块
                spriteRenderer.transform.localScale = Vector3.one; // 重置缩放
                spriteRenderer.color = color;
            }
        }

        // 隐藏高亮
        if (highlightRenderer != null)
            highlightRenderer.gameObject.SetActive(false);

        // 启用碰撞器
        if (tileCollider != null)
            tileCollider.enabled = true;

        // 设置对象名，方便在 Hierarchy 中识别
        gameObject.name = $"Tile_{row}_{col}";
    }

    /// <summary>
    /// 标记瓦片为已消除（空）。
    /// 将颜色设为透明、隐藏高亮、禁用碰撞器，使其不可见且不可点击。
    /// </summary>
    public void SetEmpty()
    {
        IsEmpty = true;
        if (spriteRenderer != null)
            spriteRenderer.color = Color.clear; // 完全透明
        if (highlightRenderer != null)
            highlightRenderer.gameObject.SetActive(false);
        if (tileCollider != null)
            tileCollider.enabled = false; // 禁用点击
    }

    /// <summary>
    /// 设置高亮状态。
    /// 显示或隐藏黄色高亮精灵，用于标识选中的瓦片。
    /// </summary>
    /// <param name="active">是否高亮</param>
    public void SetHighlight(bool active)
    {
        if (highlightRenderer != null)
            highlightRenderer.gameObject.SetActive(active);
    }

    /// <summary>
    /// 将精灵图片缩放到适配瓦片大小。
    /// 根据精灵的像素和 PPU（像素每单位）计算缩放比例。
    /// </summary>
    private void FitSpriteToTile()
    {
        if (spriteRenderer == null || spriteRenderer.sprite == null)
            return;

        Sprite s = spriteRenderer.sprite;
        float spriteWidth = s.rect.width / s.pixelsPerUnit;   // 精灵世界宽度
        float spriteHeight = s.rect.height / s.pixelsPerUnit; // 精灵世界高度
        // 缩放到 1×1 范围内（父对象的 localScale 会进一步缩放到 tileSize）
        float scale = 1f / Mathf.Max(spriteWidth, spriteHeight);
        spriteRenderer.transform.localScale = new Vector3(scale, scale, 1f);
    }

    /// <summary>
    /// 设置渲染器引用。由 GameInitializer 在创建预制体时调用。
    /// </summary>
    /// <param name="main">主精灵渲染器</param>
    /// <param name="highlight">高亮精灵渲染器</param>
    public void SetRenderers(SpriteRenderer main, SpriteRenderer highlight)
    {
        spriteRenderer = main;
        highlightRenderer = highlight;
        if (spriteRenderer != null && tileCollider == null)
            tileCollider = GetComponent<Collider2D>();
    }

    /// <summary>
    /// Unity 消息：鼠标按下时调用。
    /// 当瓦片非空且有点击回调时，触发回调通知 GridManager。
    /// 依赖 Collider2D 组件检测点击。
    /// </summary>
    private void OnMouseDown()
    {
        if (!IsEmpty && onClickCallback != null)
            onClickCallback(this);
    }
}
