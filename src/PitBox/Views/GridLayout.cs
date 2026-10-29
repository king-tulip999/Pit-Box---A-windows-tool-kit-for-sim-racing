namespace PitBox.Views;

internal sealed class GridBox
{
    public required string Id { get; init; }
    public int Col, Row, Cols = 1, Rows = 1;
}

internal static class GridLayout
{
    public static void Pack(IReadOnlyList<GridBox> order, int columns)
    {
        columns = Math.Max(1, columns);
        var used = new List<bool[]>();
        bool Free(int c, int r, GridBox b)
        {
            for (int y = r; y < r + b.Rows && y < used.Count; y++)
                for (int x = c; x < c + b.Cols; x++)
                    if (used[y][x]) return false;
            return true;
        }
        foreach (var b in order)
        {
            b.Cols = Math.Clamp(b.Cols, 1, columns);
            b.Rows = Math.Max(1, b.Rows);
            for (int r = 0; ; r++)
            {
                int c = 0;
                while (c + b.Cols <= columns && !Free(c, r, b)) c++;
                if (c + b.Cols > columns) continue;
                (b.Col, b.Row) = (c, r);
                while (used.Count < r + b.Rows) used.Add(new bool[columns]);
                for (int y = r; y < r + b.Rows; y++)
                    for (int x = c; x < c + b.Cols; x++)
                        used[y][x] = true;
                break;
            }
        }
    }

    public static void PackInRows(IReadOnlyList<GridBox> order, int columns)
    {
        columns = Math.Max(1, columns);
        int top = 0, bottom = 0, c = 0;
        foreach (var b in order)
        {
            b.Cols = Math.Clamp(b.Cols, 1, columns);
            b.Rows = Math.Max(1, b.Rows);
            if (c + b.Cols > columns)
            {
                top = bottom;
                c = 0;
            }
            (b.Col, b.Row) = (c, top);
            c += b.Cols;
            bottom = Math.Max(bottom, top + b.Rows);
        }
    }

    public static void Pack(IReadOnlyList<GridBox> order, int columns, bool inRows)
    {
        if (inRows) PackInRows(order, columns);
        else Pack(order, columns);
    }

    public static int DropIndex(IReadOnlyList<GridBox> others, GridBox moving, int columns, int col, int row, int current, double colWeight = 1, bool inRows = false)
    {
        int best = Math.Clamp(current, 0, others.Count);
        double bestDistance = double.MaxValue;
        var order = new List<GridBox>(others.Count + 1);
        for (int i = 0; i <= others.Count; i++)
        {
            order.Clear();
            for (int k = 0; k < others.Count; k++)
            {
                if (k == i) order.Add(moving);
                order.Add(others[k]);
            }
            if (i == others.Count) order.Add(moving);
            Pack(order, columns, inRows);
            double distance = Math.Abs(moving.Col - col) * colWeight + Math.Abs(moving.Row - row);
            if (distance < bestDistance - 0.01 || (Math.Abs(distance - bestDistance) < 0.01 && i == current))
                (best, bestDistance) = (i, distance);
        }
        return best;
    }

    public static int Bottom(IEnumerable<GridBox> boxes) => boxes.Select(b => b.Row + b.Rows).DefaultIfEmpty(0).Max();
}
