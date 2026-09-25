namespace BildiriKitabi.Core.Documents;

public abstract record Block;

public sealed record PageBreak : Block;

public sealed record Table(IReadOnlyList<float> ColumnWidthsPt, IReadOnlyList<TableRow> Rows) : Block;

public sealed record TableRow(IReadOnlyList<TableCell> Cells);

public sealed record TableCell(int ColumnSpan, IReadOnlyList<Block> Blocks);
