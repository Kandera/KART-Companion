using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;

namespace KARTCompanion.Tests;

/// <summary>
/// The one way this suite runs a test against real WinForms controls, and the only place any of the
/// mechanics live.
///
/// Three things have to be true before a Form can be asserted about from a test, and xUnit gives
/// none of them:
///
/// 1. <b>An STA thread.</b> xUnit's runner threads are MTA. Every WinForms control ultimately hosts
///    OLE/COM (drag-drop, the clipboard, common dialogs) and refuses to be created off an STA
///    thread, so the body runs on a thread of its own with <see cref="ApartmentState.STA"/> set
///    before it starts — the only moment it can be set.
///
/// 2. <b>Real handles.</b> A control with no HWND has no client size, no border, no scrollbars and
///    no window procedure: its layout answers are the constructor's guesses, not Windows'. Nothing
///    here asserts about a control until <see cref="RealiseHandles"/> has given it and every control
///    under it a real window. Handles are forced by touching <see cref="Control.Handle"/> rather
///    than by showing the form: <see cref="Control.CreateControl()"/> is a no-op on a Form that has
///    never been shown (it returns early for an invisible control), and actually showing a window
///    would put one on whoever is running the tests' desktop and make the result depend on what else
///    is on screen.
///
/// 3. <b>A failure that reads as a failure.</b> An exception on a thread nobody joins is a silently
///    passing test, and a body that blocks is a run that never ends. The body's exception is
///    captured and rethrown on the calling thread with
///    <see cref="ExceptionDispatchInfo"/> — so xUnit reports the original exception, message and
///    stack, exactly as if it had been thrown inline — and a body that outstays
///    <see cref="BodyTimeout"/> fails the test with a <see cref="TimeoutException"/> instead of
///    hanging the run. Exceptions raised inside a WINDOW PROCEDURE never reach that catch at all,
///    whichever way they arise; they are collected separately and rethrown by the same code. See
///    the two lines at the top of <see cref="Run{T}"/> for why, and for what those two lines cost if
///    either of them is removed.
/// </summary>
public static class WinFormsHarness
{
    /// <summary>How long a body may run before it is called a hang rather than waited on. Generous
    /// on purpose — a cold first window in a test host is slow — but finite, because the failure
    /// mode this replaces is a run that never returns.</summary>
    private static readonly TimeSpan BodyTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Runs <paramref name="body"/> on a fresh STA thread and waits for it.</summary>
    public static void Run(Action body) => Run(() => { body(); return 0; });

    /// <summary>Runs <paramref name="body"/> on a fresh STA thread and returns what it produced.</summary>
    public static T Run<T>(Func<T> body)
    {
        ExceptionDispatchInfo? fromTheBody = null;
        ExceptionDispatchInfo? fromAWindowProcedure = null;
        var result = default(T)!;

        var thread = new Thread(() =>
        {
            void Collect(object? _, ThreadExceptionEventArgs e) =>
                fromAWindowProcedure ??= ExceptionDispatchInfo.Capture(e.Exception);

            try
            {
                // The two lines below run BEFORE any window exists on this thread, and NEITHER is
                // optional.
                //
                // An exception thrown inside a window procedure does not reach the catch below. What
                // happens to it instead is decided by these two lines, and the wrong pair of answers
                // is what put ".NET error" dialogs on the maintainer's desktop and, later, crashed
                // whole test runs:
                //
                //   * WinForms' NativeWindow.Callback wraps every message it dispatches in a catch,
                //     but only ARMS that catch when the unhandled-exception mode is CatchException.
                //     Under ThrowException it installs the debuggable window procedure, which
                //     rethrows. An exception raised by a POSTED callback then unwinds through
                //     managed frames (DispatchMessage <- FPushMessageLoop <- Application.DoEvents)
                //     and does reach the catch below — but a SYNCHRONOUS send does not: it unwinds
                //     out of NativeWindow.Callback entered from native code (SetWindowPos and
                //     friends), the CLR cannot cross that reverse-P/Invoke boundary, and the TEST
                //     HOST DIES. No per-test attribution, and every other result in the run is lost.
                //     `shell.ClientSize = ...`, `Location = ...`, Dispose(), handle creation and a
                //     SendMessage all take that synchronous path, which is most of what the window
                //     tests in this suite do.
                //
                //   * CatchException arms the catch, so WinForms hands the exception to
                //     Application.ThreadException and the process stays alive. With NO subscriber
                //     that means a ThreadExceptionDialog — the box with Continue/Quit — waiting for
                //     a click no unattended run will ever give it, and a GREEN test when it comes.
                //     A subscriber suppresses that dialog unconditionally, so Collect below is the
                //     line that must never be removed; it is also what makes a mutant of the mode
                //     line above safe to build and run at all, which the ThrowException version was
                //     not.
                //
                // Together: both kinds of window-procedure exception become an ordinary red test
                // with the original stack, and nothing can put a window on anyone's screen.
                // Per-thread scope, and each Run() gets a fresh thread, so this can never race a
                // window that already exists.
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException, threadScope: true);
                Application.ThreadException += Collect;

                result = body();
            }
            catch (Exception ex)
            {
                // Captured rather than rethrown here: rethrowing on this thread would tear the test
                // host down, and swallowing it would make the test pass.
                fromTheBody = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                Application.ThreadException -= Collect;
            }
        })
        {
            IsBackground = true,
            Name = "WinForms STA test",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!thread.Join(BodyTimeout))
        {
            throw new TimeoutException(
                $"A WinForms test body did not finish within {BodyTimeout.TotalSeconds:0} seconds. " +
                "It is most likely waiting on something that needs a message loop (a modal dialog, " +
                "a Show(), an Invoke back onto a thread nothing is pumping).");
        }

        // Both are reported when both happened. A window-procedure exception does not stop the body
        // — WinForms swallows it and the next statement runs — so a body that then failed an
        // assertion of its own has TWO failures, and the one the harness collected is usually the
        // cause of the other. Neither is dropped, and a body that returned normally still fails if
        // anything was raised behind it.
        if (fromTheBody is not null && fromAWindowProcedure is not null)
        {
            throw new AggregateException(
                "A WinForms test body failed AND an exception was raised inside a window procedure. Both are below.",
                fromTheBody.SourceException, fromAWindowProcedure.SourceException);
        }

        // Throw() rethrows the ORIGINAL exception with its original stack trace appended to this
        // one's, so the failure xUnit prints is the assertion that failed and where.
        fromTheBody?.Throw();
        fromAWindowProcedure?.Throw();
        return result;
    }

    /// <summary>
    /// Builds a form on an STA thread, gives it and every control under it a real window, runs
    /// <paramref name="assertions"/> against it, and disposes it — the dispose in a finally, so a
    /// failed assertion still releases the windows rather than leaving them for the finalizer to
    /// find at some later, unrelated test's expense.
    /// </summary>
    public static void WithForm<TForm>(Func<TForm> build, Action<TForm> assertions) where TForm : Form =>
        Run(() =>
        {
            TForm? form = null;
            try
            {
                form = build();
                RealiseHandles(form);
                Pump();
                assertions(form);
            }
            finally
            {
                form?.Dispose();
                // Lets the WM_DESTROY traffic the dispose just posted drain before the thread ends,
                // so windows are actually gone by the time the next test builds its own.
                Pump();
            }
        });

    /// <summary>Forces a real window for <paramref name="control"/> and everything under it. Only
    /// controls WinForms considers current get one on their own — the shell keeps every screen's
    /// view alive and hides all but one — so this walks the whole tree rather than trusting
    /// visibility.</summary>
    public static void RealiseHandles(Control control)
    {
        _ = control.Handle;
        foreach (Control child in control.Controls) RealiseHandles(child);
    }

    /// <summary>Drains this thread's message queue. Handle creation, layout and disposal all post
    /// messages; nothing that has only been posted has happened yet.</summary>
    public static void Pump() => Application.DoEvents();

    /// <summary>
    /// Runs <paramref name="body"/> with every control in the process reporting
    /// <paramref name="dpi"/> as its <see cref="Control.DeviceDpi"/>, and puts the real number back
    /// afterwards.
    ///
    /// WHY THIS IS NOT A NICER MECHANISM: there isn't one. Under the SystemAware regime this host
    /// runs (see <see cref="TestHostConfiguration"/>) Control.DeviceDpi is the process's system DPI,
    /// which Windows fixes at startup and offers no API to move; under per-monitor awareness it is
    /// GetDpiForWindow, which is the monitor's, and a machine whose monitors are all at 100% has no
    /// way to produce any other answer. Both were MEASURED, along with the instance field behind
    /// DeviceDpi, which is not read at all in this regime. So the one number the framework keeps is
    /// moved directly.
    ///
    /// Without this, every assertion about display scaling is vacuous on a 100% machine, and the one
    /// mutant that matters — a layout that scales by a hard-coded 1.0 instead of by the control's own
    /// DPI — is alive on any such machine and on CI. That is a real, shipped defect class in this
    /// project (the history list's columns are the one part of the layout WinForms' own scaling never
    /// reaches), so it is worth a private member to pin.
    ///
    /// It fails loudly if the framework ever renames what it reaches for, rather than quietly
    /// testing nothing.
    /// </summary>
    public static void WithSystemDpiOf(int dpi, Action body)
    {
        var field = ProcessDpiField();
        var real = (int)field.GetValue(null)!;
        field.SetValue(null, dpi);
        try { body(); }
        finally { field.SetValue(null, real); }
    }

    private static FieldInfo ProcessDpiField()
    {
        var helper = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "System.Windows.Forms.Primitives")
            ?.GetType("System.Windows.Forms.DpiHelper");
        var field = helper?.GetField("<DeviceDpi>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.True(field is not null,
            "System.Windows.Forms.DpiHelper.DeviceDpi is no longer where this expects it. Display scaling "
            + "cannot be simulated any more, so every test that uses WithSystemDpiOf is now vacuous and has "
            + "to be given another way of moving the number — do not simply delete this.");
        return field!;
    }

    /// <summary>
    /// Whether anything is subscribed to <paramref name="control"/>'s MouseDown — which is the only
    /// observable trace a drag handle leaves (see Theme.MakeDragHandle).
    ///
    /// WHY NOT THE REAL MOUSE: the handler answers a left button-down by sending the FORM
    /// WM_NCLBUTTONDOWN with HTCAPTION, and Windows answers that by entering its modal window-move
    /// loop — it takes the mouse capture and does not give it back until a button-up it will never
    /// see. Synthesising the button-down to observe the handler would therefore hang the run with
    /// whoever is running the tests' mouse captured by an invisible window. The subscription is
    /// asked about instead.
    /// </summary>
    public static bool IsADragHandle(Control control) => HandlerFor(control, "s_mouseDownEvent") is not null;

    /// <summary>Raises <paramref name="control"/>'s Click, as a mouse-up over it would. Same reason
    /// as <see cref="IsADragHandle"/> for not using a real mouse: getting Windows to raise this one
    /// means taking the mouse capture first.</summary>
    public static void RaiseClick(Control control)
    {
        var handler = HandlerFor(control, "s_clickEvent") as EventHandler;
        Assert.True(handler is not null, $"Nothing is subscribed to {control.Name}'s Click, so clicking it does nothing.");
        handler!(control, EventArgs.Empty);
    }

    /// <summary>What is subscribed to one of Control's events, read out of the EventHandlerList it
    /// keeps them in. Private framework members, so this fails loudly rather than answering "nothing
    /// is subscribed" — which every caller above would read as a real finding.</summary>
    private static Delegate? HandlerFor(Control control, string eventKeyField)
    {
        var key = typeof(Control).GetField(eventKeyField, BindingFlags.NonPublic | BindingFlags.Static);
        var events = typeof(System.ComponentModel.Component)
            .GetProperty("Events", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.True(key is not null && events is not null,
            $"Control.{eventKeyField} or Component.Events is no longer where this expects it, so which events a "
            + "control has subscribers for can no longer be read — do not simply delete the assertions that use it.");
        return ((System.ComponentModel.EventHandlerList)events!.GetValue(control)!)[key!.GetValue(null)!];
    }

    /// <summary>Every control under <paramref name="root"/>, depth first, including the root.</summary>
    public static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (Control child in root.Controls)
            foreach (var descendant in Descendants(child))
                yield return descendant;
    }

    /// <summary>The one control of this type and name under <paramref name="root"/>. Fails the test
    /// rather than returning null: a test that silently found nothing to assert about is a test that
    /// passes for the wrong reason.</summary>
    public static T Find<T>(Control root, string name) where T : Control
    {
        var matches = Descendants(root).OfType<T>().Where(c => c.Name == name).ToList();
        Assert.True(matches.Count == 1,
            $"Expected exactly one {typeof(T).Name} named \"{name}\" under {root.GetType().Name}, found {matches.Count}.");
        return matches[0];
    }
}

/// <summary>
/// Puts the test host under the same DPI regime the application runs under.
///
/// Program.Main calls ApplicationConfiguration.Initialize(), whose generated body ends in
/// Application.SetHighDpiMode(HighDpiMode.SystemAware). A test host does none of that, so it starts
/// DpiUnaware — MEASURED, not assumed — and every window test was exercising the shell under a DPI
/// regime the product never uses. In a DpiUnaware process Windows lies to the whole process about
/// the display: Control.DeviceDpi is 96 on a 150% monitor, screen bounds come back in virtualised
/// coordinates, and any layout that reads either is being asked a question with a fixed answer.
///
/// A module initializer because this has to happen before the first window in the process, and the
/// first window is whatever test runs first. Nothing here assumes a particular DPI: SystemAware
/// reports whatever this machine actually is, which on a 100% display is the same 96 as before.
/// </summary>
public static class TestHostConfiguration
{
    /// <summary>Whether <see cref="Apply"/> got in before the first window. False would mean every
    /// window test is running under a different DPI regime from the product; asserted by a test
    /// rather than left to be noticed.</summary>
    public static bool HighDpiModeApplied { get; private set; }

    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Apply() => HighDpiModeApplied = Application.SetHighDpiMode(HighDpiMode.SystemAware);
}

/// <summary>
/// Whether this machine can create windows at all, probed once.
///
/// It is a probe rather than an assumption because the answer is not obvious for every place these
/// tests run: a build agent may have no interactive desktop. It is deliberately NOT a silent skip —
/// <see cref="WinFormsFactAttribute"/> turns the reason into the skip message xUnit prints, so a run
/// where these tests did not execute says so in its own output instead of reporting green.
/// </summary>
public static class WinFormsEnvironment
{
    private static readonly Lazy<string?> Reason = new(Probe);

    /// <summary>Null when windows can be created; otherwise why they cannot.</summary>
    public static string? UnavailableReason => Reason.Value;

    private static string? Probe()
    {
        try
        {
            return WinFormsHarness.Run<string?>(() =>
            {
                using var form = new Form();
                _ = form.Handle;
                return form.IsHandleCreated
                    ? null
                    : "A Form was created but Windows gave it no window handle.";
            });
        }
        catch (Exception ex)
        {
            return $"WinForms windows cannot be created here — {ex.GetType().Name}: {ex.Message}";
        }
    }
}

/// <summary>A [Fact] that needs a real window. Runs normally wherever one can be created, and skips
/// WITH THE REASON PRINTED wherever one cannot.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "xUnit1027", Justification = "Skip is set from a probe, not a constant.")]
public sealed class WinFormsFactAttribute : FactAttribute
{
    public WinFormsFactAttribute()
    {
        if (WinFormsEnvironment.UnavailableReason is { } reason) Skip = reason;
    }
}

/// <summary>
/// Every test that builds windows runs in this collection, and the collection does not parallelise.
/// Windows are per-thread and the harness gives each body its own thread, but a test host running
/// several of them at once is a host with several message queues alive at once, and the failures
/// that produces are timing-shaped rather than assertion-shaped.
/// </summary>
[CollectionDefinition(WinFormsCollection.Name, DisableParallelization = true)]
public sealed class WinFormsCollection
{
    public const string Name = "WinForms windows";
}
