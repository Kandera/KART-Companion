using System.Drawing;
using KARTCompanion.Shell;
using static KARTCompanion.Shell.ShellFrame;

namespace KARTCompanion.Tests;

/// <summary>
/// The frame geometry behind a borderless window that can still be resized. Nothing in this suite
/// constructs a Form (see HistoryExportPlanner's remarks), so the decisions are tested here and the
/// wiring — WndProc answering with these codes — is left to the maintainer's own pass over the
/// window.
/// </summary>
public class ShellFrameTests
{
    // The frame has to fit every screen it hosts, and the two dimensions are separate questions: a
    // shell of a wide screen and a tall one has to be wide AND tall, not whichever of the two
    // happened to be largest overall or came first.
    [Fact]
    public void LargestOf_TakesEachDimensionIndependently()
    {
        var largest = LargestOf(new[] { new Size(400, 300), new Size(300, 500) });

        Assert.Equal(new Size(400, 500), largest);
    }

    [Fact]
    public void LargestOf_OneSize_IsThatSize()
    {
        Assert.Equal(new Size(1042, 700), LargestOf(new[] { new Size(1042, 700) }));
    }

    // The whole middle of the window is not the frame's business — this is what leaves every control,
    // every click and every drag handle alone.
    [Fact]
    public void ResizeEdgeAt_WellInsideTheWindow_IsNotAnEdge()
    {
        Assert.Equal(FrameEdge.None, ResizeEdgeAt(new Point(500, 350), new Size(1000, 700), 6, 16));
    }

    [Theory]
    [InlineData(0, 350, FrameEdge.Left)]
    [InlineData(5, 350, FrameEdge.Left)]
    // One pixel further in and it is content again: the ring is exactly the grip margin deep, not
    // one more.
    [InlineData(6, 350, FrameEdge.None)]
    [InlineData(999, 350, FrameEdge.Right)]
    [InlineData(994, 350, FrameEdge.Right)]
    [InlineData(993, 350, FrameEdge.None)]
    [InlineData(500, 0, FrameEdge.Top)]
    [InlineData(500, 5, FrameEdge.Top)]
    [InlineData(500, 6, FrameEdge.None)]
    [InlineData(500, 699, FrameEdge.Bottom)]
    [InlineData(500, 694, FrameEdge.Bottom)]
    [InlineData(500, 693, FrameEdge.None)]
    public void ResizeEdgeAt_WithinTheGripMarginOfAnEdge_IsThatEdge(int x, int y, FrameEdge expected)
    {
        Assert.Equal(expected, ResizeEdgeAt(new Point(x, y), new Size(1000, 700), 6, 16));
    }

    // A corner is claimed from both of the edges that meet there, so it is an L reaching the corner
    // margin along each one — not just the 6x6 square where the two grip margins overlap, which is a
    // target a mouse cannot be expected to find.
    [Theory]
    [InlineData(2, 2, FrameEdge.TopLeft)]
    [InlineData(15, 2, FrameEdge.TopLeft)]
    [InlineData(2, 15, FrameEdge.TopLeft)]
    [InlineData(998, 2, FrameEdge.TopRight)]
    [InlineData(985, 2, FrameEdge.TopRight)]
    [InlineData(998, 15, FrameEdge.TopRight)]
    [InlineData(2, 698, FrameEdge.BottomLeft)]
    [InlineData(15, 698, FrameEdge.BottomLeft)]
    [InlineData(2, 685, FrameEdge.BottomLeft)]
    [InlineData(998, 698, FrameEdge.BottomRight)]
    [InlineData(985, 698, FrameEdge.BottomRight)]
    [InlineData(998, 685, FrameEdge.BottomRight)]
    public void ResizeEdgeAt_WithinTheCornerMarginOfBothEdges_IsThatCorner(int x, int y, FrameEdge expected)
    {
        Assert.Equal(expected, ResizeEdgeAt(new Point(x, y), new Size(1000, 700), 6, 16));
    }

    // Just past the corner margin the edge takes over again, so the corner cannot quietly swallow
    // the whole edge.
    [Theory]
    [InlineData(16, 2, FrameEdge.Top)]
    [InlineData(2, 16, FrameEdge.Left)]
    [InlineData(983, 2, FrameEdge.Top)]
    [InlineData(998, 683, FrameEdge.Right)]
    public void ResizeEdgeAt_PastTheCornerMargin_IsTheEdgeAgain(int x, int y, FrameEdge expected)
    {
        Assert.Equal(expected, ResizeEdgeAt(new Point(x, y), new Size(1000, 700), 6, 16));
    }

    // A point that is not on the window at all must claim nothing. Without the guard an x of -1 is
    // still "less than the grip margin" and would report the left edge for a cursor that is over
    // some other window entirely.
    [Theory]
    [InlineData(-1, 350)]
    [InlineData(1000, 350)]
    [InlineData(500, -1)]
    [InlineData(500, 700)]
    public void ResizeEdgeAt_OutsideTheClientArea_IsNotAnEdge(int x, int y)
    {
        Assert.Equal(FrameEdge.None, ResizeEdgeAt(new Point(x, y), new Size(1000, 700), 6, 16));
    }

    // These numbers are Windows', not ours: WM_NCHITTEST answers HTLEFT with 10 and the rest follow
    // it. Asserted as literals for that reason — an assertion written against our own constant would
    // agree with any value they were given.
    [Theory]
    [InlineData(FrameEdge.Left, 10)]
    [InlineData(FrameEdge.Right, 11)]
    [InlineData(FrameEdge.Top, 12)]
    [InlineData(FrameEdge.TopLeft, 13)]
    [InlineData(FrameEdge.TopRight, 14)]
    [InlineData(FrameEdge.Bottom, 15)]
    [InlineData(FrameEdge.BottomLeft, 16)]
    [InlineData(FrameEdge.BottomRight, 17)]
    [InlineData(FrameEdge.None, 1)]
    public void HitTestCode_IsTheWin32CodeForThatEdge(FrameEdge edge, int expected)
    {
        Assert.Equal(expected, HitTestCode(edge));
    }

    [Fact]
    public void PointFromLParam_ReadsTheTwoHalvesAsXAndY()
    {
        Assert.Equal(new Point(300, 200), PointFromLParam(Pack(300, 200)));
    }

    // Both halves are SIGNED. A monitor arranged to the left of the primary one has negative screen
    // coordinates, and read unsigned that cursor lands 65,000 pixels to the right instead — the
    // frame would then answer "not an edge" for every point on such a display.
    [Fact]
    public void PointFromLParam_NegativeScreenCoordinates_StaySigned()
    {
        Assert.Equal(new Point(-40, -12), PointFromLParam(Pack(-40, -12)));
    }

    private static IntPtr Pack(int x, int y) => (IntPtr)(((y & 0xFFFF) << 16) | (x & 0xFFFF));
}
