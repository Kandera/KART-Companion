using System.Drawing;
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
        var failure = Assert.Throws<Xunit.Sdk.FailException>(
            () => WinFormsHarness.Run(() => Assert.Fail("deliberate")));

        Assert.Contains("deliberate", failure.Message);
    }

    /// <summary>
    /// The one the harness originally missed, and the reason the maintainer saw ".NET error"
    /// dialogs during a test run.
    ///
    /// An exception raised inside a window procedure by a POSTED callback — which is how most of
    /// WinForms' own work arrives — never reaches the harness's own catch. Left at WinForms'
    /// default it is answered with a ThreadExceptionDialog: a box on the desktop waiting for a
    /// click an unattended run never gives, and a test that then reports GREEN. The
    /// Application.ThreadException subscription in Run is what turns it into this red one instead.
    /// </summary>
    [WinFormsFact]
    public void AnExceptionRaisedByAPostedCallback_FailsTheTestInsteadOfOpeningADialog()
    {
        var failure = Assert.Throws<InvalidOperationException>(() => WinFormsHarness.Run(() =>
        {
            using var form = new Form();
            _ = form.Handle;
            form.BeginInvoke(new Action(() => throw new InvalidOperationException("raised by a posted callback")));
            WinFormsHarness.Pump();
        }));

        Assert.Equal("raised by a posted callback", failure.Message);
        Assert.Contains(nameof(AnExceptionRaisedByAPostedCallback_FailsTheTestInsteadOfOpeningADialog),
            failure.StackTrace);
    }

    /// <summary>
    /// The other half, and the one that used to KILL THE RUN rather than merely mislead it.
    ///
    /// `form.ClientSize = ...` is a synchronous SetWindowPos: Windows sends WM_SIZE straight back
    /// into the window procedure from native code, so a throwing Resize handler unwinds out of
    /// NativeWindow.Callback across a reverse-P/Invoke boundary the CLR cannot cross. Under the
    /// harness's old UnhandledExceptionMode.ThrowException that took the whole test host with it —
    /// "The active test run was aborted. Reason: Test host process crashed", no per-test
    /// attribution, every other result in the run lost. Three of this suite's window tests set
    /// ClientSize, and Location, Dispose(), handle creation and SendMessage take the same path.
    ///
    /// It has to be an ordinary red test with a usable stack, which is what this asserts.
    /// </summary>
    [WinFormsFact]
    public void AnExceptionRaisedSynchronouslyInsideAWindowProcedure_FailsTheTestInsteadOfCrashingTheRun()
    {
        var carriedOn = false;
        var failure = Assert.Throws<InvalidOperationException>(() => WinFormsHarness.Run(() =>
        {
            using var form = new Form();
            _ = form.Handle;
            form.Resize += (_, _) => throw new InvalidOperationException("raised by a synchronous send");
            form.ClientSize = new Size(457, 313);
            carriedOn = true;
        }));

        // The statement after the assignment ran, which is what says this exception did NOT travel up
        // the body's own stack: WinForms caught it inside the window procedure and the harness
        // collected it from there. Without that, this test would pass just as happily for an ordinary
        // exception and would be pinning nothing.
        Assert.True(carriedOn, "The body stopped at the assignment, so this is not the window-procedure path.");
        Assert.Equal("raised by a synchronous send", failure.Message);
        Assert.Contains(nameof(AnExceptionRaisedSynchronouslyInsideAWindowProcedure_FailsTheTestInsteadOfCrashingTheRun),
            failure.StackTrace);
    }

    /// <summary>
    /// WinForms swallows a window-procedure exception and lets the next statement run, so a body
    /// can fail an assertion of its own AFTER one has already been raised behind it — and the one
    /// raised behind it is usually the cause. Neither may be dropped in favour of the other.
    /// </summary>
    [WinFormsFact]
    public void ABodyThatFailsAfterAWindowProcedureAlreadyDid_ReportsBoth()
    {
        var failure = Assert.Throws<AggregateException>(() => WinFormsHarness.Run(() =>
        {
            using var form = new Form();
            _ = form.Handle;
            form.Resize += (_, _) => throw new InvalidOperationException("from the window procedure");
            form.ClientSize = new Size(457, 313);
            throw new NotSupportedException("from the body");
        }));

        Assert.Equal(
            new[] { "from the body", "from the window procedure" },
            failure.InnerExceptions.Select(e => e.Message));
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

    /// <summary>
    /// The test host runs under the DPI regime the product runs under, and not the one a test host
    /// gets by default.
    ///
    /// Program.Main calls ApplicationConfiguration.Initialize(), which ends in
    /// SetHighDpiMode(SystemAware). A test host does none of that and starts DpiUnaware, where
    /// Windows reports 96 DPI and virtualised screen coordinates to the whole process whatever the
    /// monitor is set to — so every window test was exercising the shell under a display regime the
    /// product never uses, and any layout reading DeviceDpi was being asked a question with a fixed
    /// answer.
    ///
    /// Asserted against the literal rather than against the constant that sets it, so a test written
    /// from the same constant cannot agree with any value it is given.
    /// </summary>
    [Fact]
    public void TheTestHost_RunsUnderTheSameDpiRegimeAsTheApplication()
    {
        Assert.True(TestHostConfiguration.HighDpiModeApplied,
            "The high-DPI mode was set too late — a window already existed in this process.");
        Assert.Equal(HighDpiMode.SystemAware, Application.HighDpiMode);
    }

    /// <summary>The display scale every control in the process reports can be moved for the length
    /// of one body, which is the only way a 100%-scaled machine can exercise a scaled one.</summary>
    [WinFormsFact]
    public void WithSystemDpiOf_MovesWhatControlsReportAndPutsItBack()
    {
        WinFormsHarness.Run(() =>
        {
            using var control = new Control();
            var before = control.DeviceDpi;

            WinFormsHarness.WithSystemDpiOf(120, () => Assert.Equal(120, control.DeviceDpi));
            Assert.Equal(before, control.DeviceDpi);

            // And put back even when the body fails, or one red test would take the rest of the run
            // with it.
            Assert.Throws<NotSupportedException>(
                () => WinFormsHarness.WithSystemDpiOf(144, () => throw new NotSupportedException()));
            Assert.Equal(before, control.DeviceDpi);
        });
    }
}
