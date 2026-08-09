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
///    hanging the run.
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
        ExceptionDispatchInfo? failure = null;
        var result = default(T)!;

        var thread = new Thread(() =>
        {
            try
            {
                result = body();
            }
            catch (Exception ex)
            {
                // Captured rather than rethrown here: rethrowing on this thread would tear the test
                // host down, and swallowing it would make the test pass.
                failure = ExceptionDispatchInfo.Capture(ex);
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

        // Throw() rethrows the ORIGINAL exception with its original stack trace appended to this
        // one's, so the failure xUnit prints is the assertion that failed and where.
        failure?.Throw();
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
