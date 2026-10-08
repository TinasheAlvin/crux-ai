using VhonaAI.Core.Calling;

namespace VhonaAI.Tests;

public class RandAmountsTests
{
    [Fact]
    public void Card_amounts_round_to_whole_rand_unless_every_value_has_cents()
    {
        Assert.False(RandAmounts.CentsMatter(6039.50m, 5570m, 469.50m));
        Assert.Equal("R6 040", RandAmounts.FormatCard(6039.50m, 6039.50m, 5570m, 469.50m));
        Assert.Equal("R470", RandAmounts.FormatCard(469.50m, 6039.50m, 5570m, 469.50m));
        Assert.Equal("-R6 040", RandAmounts.FormatCard(-6039.50m, 6039.50m, 5570m, 469.50m));

        Assert.True(RandAmounts.CentsMatter(6039.50m, 5570.25m, 469.25m));
        Assert.Equal("R6 039.50", RandAmounts.FormatCard(6039.50m, 6039.50m, 5570.25m, 469.25m));
        Assert.Equal("R469.25", RandAmounts.FormatCard(469.25m, 6039.50m, 5570.25m, 469.25m));
    }
}
