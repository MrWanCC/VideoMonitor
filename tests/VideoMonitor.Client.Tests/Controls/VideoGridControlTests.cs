using VideoMonitor.Client.Controls;

namespace VideoMonitor.Client.Tests.Controls;

public sealed class VideoGridControlTests
{
    [Fact]
    public void CreateSecondaryGrid_UsesOneRowAndThreeEqualPercentColumns()
    {
        using var grid = VideoGridControl.CreateSecondaryGrid();

        Assert.Equal(1, grid.Grid.RowCount);
        Assert.Equal(3, grid.Grid.ColumnCount);
        Assert.All(grid.Grid.ColumnStyles.Cast<ColumnStyle>(), style =>
        {
            Assert.Equal(SizeType.Percent, style.SizeType);
            Assert.Equal(33.333f, style.Width, 3);
        });
    }

    [Fact]
    public void CreateMainGrid_UsesTwoEqualRowsAndColumns()
    {
        using var grid = VideoGridControl.CreateMainGrid();

        Assert.Equal(2, grid.Grid.RowCount);
        Assert.Equal(2, grid.Grid.ColumnCount);
        Assert.All(grid.Grid.RowStyles.Cast<RowStyle>(), style => Assert.Equal(50f, style.Height));
        Assert.All(grid.Grid.ColumnStyles.Cast<ColumnStyle>(), style => Assert.Equal(50f, style.Width));
    }
}
