namespace PixelArtConverter;

public static class SpriteLayout
{
    public static List<Rectangle> GetCells(int imageWidth, int imageHeight, Settings s)
    {
        if (s.Columns < 1 || s.Rows < 1 || s.Width < 1 || s.Height < 1)
            throw new ArgumentException("分割数・出力サイズは1以上が必要です。");
        if (s.Padding < 0 || s.Padding * 2 >= Math.Min(s.Width, s.Height))
            throw new ArgumentException("出力余白は出力サイズの半分未満にしてください。");
        if (s.MarginLeft < 0 || s.MarginTop < 0 || s.MarginRight < 0 || s.MarginBottom < 0)
            throw new ArgumentException("入力余白は0以上にしてください。");
        int[] xs = Edges(s.MarginLeft, imageWidth - s.MarginRight, s.Columns, s.ColumnWidths, "列の幅");
        int[] ys = Edges(s.MarginTop, imageHeight - s.MarginBottom, s.Rows, s.RowHeights, "行の高さ");
        var cells = new List<Rectangle>();
        for (int row = 0; row < s.Rows; row++)
        for (int col = 0; col < s.Columns; col++)
            cells.Add(Rectangle.FromLTRB(xs[col], ys[row], xs[col + 1], ys[row + 1]));
        return cells;
    }
    static int[] Edges(int start, int end, int count, string manual, string axis)
    {
        if (end - start < count) throw new ArgumentException($"{axis}方向の余白が大きすぎます。");
        var edges = new int[count + 1]; edges[0] = start; edges[count] = end;
        if (string.IsNullOrWhiteSpace(manual))
            for (int i = 1; i < count; i++) edges[i] = start + (int)((long)i * (end - start) / count);
        else
        {
            var values = manual.Split(new[] { ',', '、', ' ', ';', '；', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (values.Length != count - 1 && values.Length != count)
                throw new ArgumentException($"{axis}は{count}個入力してください。{count - 1}個なら最後は残りの範囲を使います。");
            long position = start;
            for (int i = 0; i < values.Length; i++)
            {
                if (!int.TryParse(values[i], out int value) || value <= 0)
                    throw new ArgumentException($"{axis}は1以上の整数で指定してください。");
                position += value;
                if (position > end || (i < count - 1 && position >= end))
                    throw new ArgumentException($"{axis}の合計が余白を除いた範囲を超えるか、最後のマスがなくなっています。");
                edges[i + 1] = (int)position;
            }
        }
        for (int i = 1; i < edges.Length; i++)
            if (edges[i] <= edges[i - 1]) throw new ArgumentException($"{axis}分割線はの合計と入力範囲を確認してください。");
        return edges;
    }
}
