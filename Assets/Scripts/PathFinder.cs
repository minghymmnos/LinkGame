using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 路径查找器。
/// 核心算法类，检测两个瓦片之间是否存在可连通的路径。
/// 连连看的连通规则：两个相同类型瓦片之间的路径最多允许 2 个拐角（即 0、1 或 2 次转弯）。
/// 这是一个静态工具类，无需挂载到 GameObject 上。
/// </summary>
public class PathFinder
{
    /// <summary>
    /// 网格坐标点结构体。
    /// 使用行列索引表示网格中的位置。
    /// </summary>
    public struct Point
    {
        public int Row; // 行索引
        public int Col; // 列索引

        public Point(int row, int col)
        {
            Row = row;
            Col = col;
        }
    }

    /// <summary>
    /// 主入口：查找两个点之间的可连通路径。
    /// 按复杂度递增的顺序尝试三种连接方式，返回第一个找到的路径。
    /// </summary>
    /// <param name="grid">网格数据，int? 类型，null 表示空格可通行</param>
    /// <param name="rows">网格行数</param>
    /// <param name="cols">网格列数</param>
    /// <param name="start">起点</param>
    /// <param name="end">终点</param>
    /// <returns>路径点列表（含起止点），无路径则返回 null</returns>
    public static List<Point> FindPath(int?[,] grid, int rows, int cols, Point start, Point end)
    {
        // 起点和终点是同一个位置，无意义
        if (start.Row == end.Row && start.Col == end.Col)
            return null;

        // 必须是同类型才能消除
        int valueAtStart = grid[start.Row, start.Col].Value;
        int valueAtEnd = grid[end.Row, end.Col].Value;
        if (valueAtStart != valueAtEnd)
            return null;

        // 尝试 0 拐角（直线连接）— 最简单，优先级最高
        List<Point> directPath = TryDirectConnect(grid, rows, cols, start, end);
        if (directPath != null)
            return directPath;

        // 尝试 1 拐角连接
        List<Point> oneTurnPath = TryOneTurnConnect(grid, rows, cols, start, end);
        if (oneTurnPath != null)
            return oneTurnPath;

        // 尝试 2 拐角连接
        List<Point> twoTurnPath = TryTwoTurnConnect(grid, rows, cols, start, end);
        if (twoTurnPath != null)
            return twoTurnPath;

        return null; // 三种方式都失败，不可连通
    }

    /// <summary>
    /// 判断指定格子是否可通行。
    /// 起点和终点格子特殊处理（即使有瓦片也视为可通行），
    /// 其他格子必须为空（grid 值为 null）才可通行。
    /// </summary>
    /// <param name="grid">网格数据</param>
    /// <param name="row">行索引</param>
    /// <param name="col">列索引</param>
    /// <param name="start">路径起点</param>
    /// <param name="end">路径终点</param>
    /// <returns>是否可通行</returns>
    private static bool IsCellPassable(int?[,] grid, int row, int col, Point start, Point end)
    {
        // 起点格子始终可通行（瓦片本身不算障碍）
        if (row == start.Row && col == start.Col)
            return true;
        // 终点格子始终可通行
        if (row == end.Row && col == end.Col)
            return true;
        // 其他格子：只有空格（null）才可通行
        return grid[row, col] == null;
    }

    /// <summary>
    /// 尝试直线连接（0 拐角）。
    /// 起点和终点必须在同一行或同一列，且中间所有格子为空。
    /// </summary>
    /// <returns>路径 [起点, 终点]（2 个点），不可连通则 null</returns>
    private static List<Point> TryDirectConnect(int?[,] grid, int rows, int cols, Point start, Point end)
    {
        // 同行：检查列方向中间格子
        if (start.Row == end.Row)
        {
            int minCol = Mathf.Min(start.Col, end.Col);
            int maxCol = Mathf.Max(start.Col, end.Col);
            for (int c = minCol + 1; c < maxCol; c++)
            {
                if (!IsCellPassable(grid, start.Row, c, start, end))
                    return null; // 中间有障碍，不可连通
            }
            return new List<Point> { start, end };
        }

        // 同列：检查行方向中间格子
        if (start.Col == end.Col)
        {
            int minRow = Mathf.Min(start.Row, end.Row);
            int maxRow = Mathf.Max(start.Row, end.Row);
            for (int r = minRow + 1; r < maxRow; r++)
            {
                if (!IsCellPassable(grid, r, start.Col, start, end))
                    return null;
            }
            return new List<Point> { start, end };
        }

        return null; // 既不同行也不同列，无法直线连接
    }

    /// <summary>
    /// 尝试 1 拐角连接。
    /// 构造两个可能的拐点（L 形路径），检查拐点和两条直线段是否都通畅。
    /// 拐点1: (start.Row, end.Col) — 先水平后垂直
    /// 拐点2: (end.Row, start.Col) — 先垂直后水平
    /// </summary>
    /// <returns>路径 [起点, 拐点, 终点]（3 个点），不可连通则 null</returns>
    private static List<Point> TryOneTurnConnect(int?[,] grid, int rows, int cols, Point start, Point end)
    {
        // 尝试拐点1：(start.Row, end.Col)
        Point corner1 = new Point(start.Row, end.Col);
        if (IsCellPassable(grid, corner1.Row, corner1.Col, start, end))
        {
            // 检查 起点→拐点1 和 拐点1→终点 两段直线是否都通畅
            List<Point> path1 = TryDirectConnect(grid, rows, cols, start, corner1);
            if (path1 != null)
            {
                List<Point> path2 = TryDirectConnect(grid, rows, cols, corner1, end);
                if (path2 != null)
                {
                    return new List<Point> { start, corner1, end };
                }
            }
        }

        // 尝试拐点2：(end.Row, start.Col)
        Point corner2 = new Point(end.Row, start.Col);
        if (IsCellPassable(grid, corner2.Row, corner2.Col, start, end))
        {
            List<Point> path1 = TryDirectConnect(grid, rows, cols, start, corner2);
            if (path1 != null)
            {
                List<Point> path2 = TryDirectConnect(grid, rows, cols, corner2, end);
                if (path2 != null)
                {
                    return new List<Point> { start, corner2, end };
                }
            }
        }

        return null; // 两个拐点都不可行
    }

    /// <summary>
    /// 尝试 2 拐角连接（Z 形或 U 形路径）。
    /// 通过遍历所有可能的中间行或列，构造两个拐点，检查三段直线是否都通畅。
    /// 遍历范围包含边界外的虚拟区域（0 和 rows+1 / cols+1），使路径可绕到棋盘外部。
    /// </summary>
    /// <returns>路径 [起点, 拐点1, 拐点2, 终点]（4 个点），不可连通则 null</returns>
    private static List<Point> TryTwoTurnConnect(int?[,] grid, int rows, int cols, Point start, Point end)
    {
        // ---------- 方式1：水平扫描（固定中间行，两个拐点在同一行）----------
        // 拐点1: (r, start.Col)，拐点2: (r, end.Col)
        for (int r = 0; r <= rows + 1; r++)
        {
            // 跳过与起止点同行的行（那样就变成 1 拐角或直线了，已在前面处理）
            if (r == start.Row || r == end.Row)
                continue;

            Point corner1 = new Point(r, start.Col);
            Point corner2 = new Point(r, end.Col);

            if (IsCellPassable(grid, corner1.Row, corner1.Col, start, end) &&
                IsCellPassable(grid, corner2.Row, corner2.Col, start, end))
            {
                // 检查三段直线：起点→拐点1，拐点1→拐点2，拐点2→终点
                List<Point> sToC1 = TryDirectConnect(grid, rows, cols, start, corner1);
                if (sToC1 == null) continue;

                List<Point> c1ToC2 = TryDirectConnect(grid, rows, cols, corner1, corner2);
                if (c1ToC2 == null) continue;

                List<Point> c2ToE = TryDirectConnect(grid, rows, cols, corner2, end);
                if (c2ToE == null) continue;

                return new List<Point> { start, corner1, corner2, end };
            }
        }

        // ---------- 方式2：垂直扫描（固定中间列，两个拐点在同一列）----------
        // 拐点1: (start.Row, c)，拐点2: (end.Row, c)
        for (int c = 0; c <= cols + 1; c++)
        {
            if (c == start.Col || c == end.Col)
                continue;

            Point corner1 = new Point(start.Row, c);
            Point corner2 = new Point(end.Row, c);

            if (IsCellPassable(grid, corner1.Row, corner1.Col, start, end) &&
                IsCellPassable(grid, corner2.Row, corner2.Col, start, end))
            {
                List<Point> sToC1 = TryDirectConnect(grid, rows, cols, start, corner1);
                if (sToC1 == null) continue;

                List<Point> c1ToC2 = TryDirectConnect(grid, rows, cols, corner1, corner2);
                if (c1ToC2 == null) continue;

                List<Point> c2ToE = TryDirectConnect(grid, rows, cols, corner2, end);
                if (c2ToE == null) continue;

                return new List<Point> { start, corner1, corner2, end };
            }
        }

        return null; // 两种扫描方式都失败
    }
}
