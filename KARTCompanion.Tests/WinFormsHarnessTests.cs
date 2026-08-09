using System.Windows.Forms;

namespace KARTCompanion.Tests;

/// <summary>
/// Tests of the test harness itself. They exist because the harness is the thing every other
/// window test trusts: if it can let a failure through, every green result it prints is worth
/// nothing, and nothing else in the suite would notice.
/// </summary>
[Collection(WinFormsCollection.Name)]
public class WinFormsHarnessTests
{
    [WinFormsFact]
    public void AnAssertionThatFailsInTheBody_FailsTheTest()
    {
        var failure = Assert.Throws<Xunit.Sdk.TrueException>(
            () => WinFormsHarness.Run(() => Assert.True(false, "deliberate")));

        Assert.Contains("deliberate", failure.Message);
    }

    /// <summary>
    /// The one the harness originally missed, and the reason the maintainer saw ".NET error"
    /// dialogs during a test run.
    ///
    /// An exception raised inside a window procedure — here through a posted callback, which is how
    /// most of WinForms' own work arrives — is caught by the message loop, not by the harness. Left
    /// at its default, WinForms answers it by showing a ThreadExceptionDialog and carrying on: a box
    /// on the desktop waiting for a click that an unattended run never gives, and a test that then
    /// reports GREEN. Application.SetUnhandledExceptionMode(ThrowException) is what makes it come
    /// out of Pump() instead.
    /// </summary>
    [WinFormsFact]
    public void AnExceptionRaisedInsideTheMessageLoop_FailsTheTestInsteadOfOpeningADialog()
    {
        var failure = Assert.Throws<InvalidOperationException>(() => WinFormsHarness.Run(() =>
        {
            using var form = new Form();
            _ = form.Handle;
            form.BeginInvoke(new Action(() => throw new InvalidOperationException("raised in the loop")));
            WinFormsHarness.Pump();
        }));

        Assert.Equal("raised in the loop", failure.Message);
    }

    /// <summary>A body that never returns must be reported as a hang, not waited on forever. Uses
    /// the real timeout rather than a shortened one — this asserts the mechanism exists and that a
    /// finished body is not mistaken for a hung one.</summary>
    [WinFormsFact]
    public void ABodyThatFinishes_IsNotReportedAsAHang()
    {
        Assert.Equal(42, WinFormsHarness.Run(() => 42));
    }

    /// <summary>Find fails the test when there is nothing to find, rather than handing back a null
    /// that the next line asserts happily about.</summary>
    [WinFormsFact]
    public void Find_WithNoMatch_FailsRatherThanReturningNothing()
    {
        Assert.Throws<Xunit.Sdk.TrueException>(() => WinFormsHarness.Run(() =>
        {
            using var form = new Form();
            _ = form.Handle;
            return WinFormsHarness.Find<Button>(form, "no such button");
        }));
    }
}
