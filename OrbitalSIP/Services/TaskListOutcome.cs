using System.Collections.Generic;
using System.Linq;

namespace OrbitalSIP.Services
{
    /// <summary>What one request the load considered making came back with.</summary>
    public enum TaskFetch
    {
        /// <summary>Never made. Contributes nothing either way.</summary>
        Skipped,

        /// <summary>Made, and did not answer.</summary>
        Failed,

        /// <summary>Made, and answered.</summary>
        Answered,
    }

    /// <summary>What the tasks screen has to say after a load.</summary>
    public enum TaskListState
    {
        /// <summary>Tasks came back: show them.</summary>
        Ready,

        /// <summary>Asked, answered, nothing there.</summary>
        Empty,

        /// <summary>
        /// The operator may not see these tasks — the backend refused the read, or the
        /// session carries no user id the tasks API can be asked about, which is the same
        /// answer arrived at without a request.
        /// </summary>
        Refused,

        /// <summary>
        /// There is no session to ask with. Told apart from <see cref="Refused"/> because
        /// the operator can do something about this one, and because the wording for
        /// refused is permanent — "not for you" — which is the wrong thing to say to
        /// someone whose token merely expired.
        /// </summary>
        Expired,

        /// <summary>Nothing was learned, so nothing may be claimed about the list.</summary>
        Failed,
    }

    /// <summary>
    /// Which of the four things the tasks screen is looking at, given what its requests
    /// came back with.
    ///
    /// Out here rather than inside the view because it is the hard decision on that screen
    /// and every bug found in it so far was a bug in this reasoning: an account that cannot
    /// be assigned tasks reading as a transient failure, a 403 from a write turning a good
    /// list into "no access", an unexpected exception saying nothing at all. Four states
    /// that have to be told apart is exactly the kind of thing a test should be able to
    /// reach, and inside a UserControl nothing could.
    /// </summary>
    public static class TaskListOutcome
    {
        /// <summary>
        /// The order of the rules is the whole content of this method.
        ///
        /// A dead session comes before everything, because every other signal is downstream
        /// of it. <paramref name="unassignable"/> in particular is computed from the token's
        /// sub, and EndSession nulls the whole decoded token — so an expired session reads
        /// as unassignable and would be reported as "not for you", permanently worded, to
        /// an operator whose only problem is that they need to sign in again. Reachable
        /// today: a session dying behind an active call defers the login screen, the
        /// operator stays on the call, and the Tasks tab is reachable from there.
        ///
        /// Then a refusal, ahead of any failed request, because it is a definite
        /// answer and the request that carried it necessarily failed too: a 403 is both,
        /// and reading it as "could not load" would put a retry-shaped sentence next to a
        /// refresh button that can never help. <paramref name="unassignable"/> joins it for
        /// the same reason — nothing was asked because there is nothing to ask about, the
        /// session's own id is missing, and that will not change before the session does.
        /// NavBadgeService keeps the two apart for its own purposes; from here they are one
        /// answer: not for you.
        ///
        /// Then any request that did not answer, however many did: half a merge is not the
        /// operator's open tasks, and the missing half is silent — no row says it is absent.
        ///
        /// Then the case where nothing was asked and no reason was given. It should not
        /// arise, and if it does, "could not load" is the honest end of it: nobody learned
        /// there were no tasks.
        /// </summary>
        /// <param name="fetches">One entry per request the load considered making.</param>
        /// <param name="taskCount">Rows in hand after the responses were merged.</param>
        /// <param name="signedOut">No token to ask with — the session ended or never began.</param>
        public static TaskListState Of(IReadOnlyList<TaskFetch> fetches, int taskCount,
                                       bool forbidden, bool unassignable, bool signedOut)
        {
            if (signedOut) return TaskListState.Expired;
            if (forbidden || unassignable) return TaskListState.Refused;
            if (fetches.Any(fetch => fetch == TaskFetch.Failed)) return TaskListState.Failed;
            if (!fetches.Any(fetch => fetch == TaskFetch.Answered)) return TaskListState.Failed;

            return taskCount > 0 ? TaskListState.Ready : TaskListState.Empty;
        }
    }
}
