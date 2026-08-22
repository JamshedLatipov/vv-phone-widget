using System;
using System.Collections.Generic;
using System.Linq;
using OrbitalSIP.Models;

namespace OrbitalSIP.Services
{
    /// <summary>What the transfer panel shows right now.</summary>
    public enum TransferPanelState
    {
        Loading,

        /// <summary>The load failed. Offer a retry.</summary>
        Error,

        /// <summary>403 or 404 from the backend. A retry will not help — manual dialing is what remains.</summary>
        Forbidden,

        /// <summary>The backend answered, but there is no one to transfer to.</summary>
        Empty,

        Ready,
    }

    /// <summary>
    /// All of the transfer panel's decision logic, testable without a window.
    /// Same split as <see cref="LeadCallPanelPresenter"/>: the view only
    /// paints what was decided here.
    /// </summary>
    public static class TransferTargetsPresenter
    {
        /// <summary>Hint for a queue the backend marked unavailable for a reason this build does not recognise.</summary>
        public const string QueueUnavailableKey = "TransferQueueUnavailable";

        public static TransferPanelState SelectState(
            TransferTargets? targets,
            bool loading,
            string? error,
            bool forbidden)
        {
            if (loading) return TransferPanelState.Loading;
            if (forbidden) return TransferPanelState.Forbidden;
            if (targets == null) return TransferPanelState.Error;
            if (!string.IsNullOrEmpty(error)) return TransferPanelState.Error;
            // A missing or explicitly-null list on the wire binds straight
            // through to null (see TransferTargets) — treat that as empty
            // rather than let .Count throw.
            return (targets.Operators?.Count ?? 0) == 0 && (targets.Queues?.Count ?? 0) == 0
                ? TransferPanelState.Empty
                : TransferPanelState.Ready;
        }

        public static IReadOnlyList<TransferOperatorTarget> FilterOperators(
            TransferTargets? targets,
            string query,
            string? ownExtension)
        {
            if (targets?.Operators == null) return [];
            var q = (query ?? string.Empty).Trim();

            return targets.Operators
                // Extension is an identifier, so self-exclusion compares it
                // ordinally. The free-text search two lines down runs over
                // human names as well as extensions and deliberately uses
                // culture-aware comparison instead — harmless today, since
                // extensions are digit-only (see ActiveCallView's "never a
                // SIP endpoint id" note), but the two rules are intentional,
                // not an oversight.
                .Where(o => !string.Equals(o.Extension, ownExtension, StringComparison.OrdinalIgnoreCase))
                .Where(o => q.Length == 0 || Matches(o.FullName, q) || Matches(o.Extension, q))
                .OrderBy(o => o.FullName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public static IReadOnlyList<TransferQueueTarget> FilterQueues(
            TransferTargets? targets,
            string query)
        {
            if (targets?.Queues == null) return [];
            var q = (query ?? string.Empty).Trim();

            return targets.Queues
                .Where(x => q.Length == 0 || Matches(x.Name, q) || Matches(x.Description, q))
                // Unavailable rows sink to the bottom instead of being
                // dropped: the operator should still see that the queue
                // exists, and why it is greyed out.
                .OrderBy(x => string.IsNullOrEmpty(x.DisabledReason) ? 0 : 1)
                .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// The i18n key for an unavailable queue's hint, or null when the
        /// queue is available. An unrecognised reason is not left without
        /// text — otherwise a value added on the backend later would show up
        /// as a blank hint.
        /// </summary>
        public static string? QueueDisabledKey(string? disabledReason) =>
            string.IsNullOrWhiteSpace(disabledReason)
                ? null
                : disabledReason switch
                {
                    "unsafe-name" => "TransferQueueUnsafeName",
                    _ => QueueUnavailableKey,
                };

        private static bool Matches(string? value, string query) =>
            !string.IsNullOrEmpty(value)
            && value.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }
}
