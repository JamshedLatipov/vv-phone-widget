using System;
using System.Collections.Generic;
using System.Linq;
using OrbitalSIP.Models;

namespace OrbitalSIP.Services
{
    /// <summary>Что панель перевода показывает прямо сейчас.</summary>
    public enum TransferPanelState
    {
        Loading,

        /// <summary>Загрузка не удалась. Предлагаем повтор.</summary>
        Error,

        /// <summary>403 или 404 от бэка. Повтор не поможет — остаётся ручной ввод.</summary>
        Forbidden,

        /// <summary>Бэк ответил, но переводить некому.</summary>
        Empty,

        Ready,
    }

    /// <summary>
    /// Вся логика панели перевода, которую можно проверить без окна.
    /// Тот же приём, что и в <see cref="LeadCallPanelPresenter"/>: вид только
    /// рисует то, что решили здесь.
    /// </summary>
    public static class TransferTargetsPresenter
    {
        /// <summary>Подсказка для очереди, которую бэк пометил недоступной, но причину назвал незнакомую.</summary>
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
            return targets.Operators.Count == 0 && targets.Queues.Count == 0
                ? TransferPanelState.Empty
                : TransferPanelState.Ready;
        }

        public static IReadOnlyList<TransferOperatorTarget> FilterOperators(
            TransferTargets? targets,
            string query,
            string? ownExtension)
        {
            if (targets == null) return [];
            var q = (query ?? string.Empty).Trim();

            return targets.Operators
                .Where(o => !string.Equals(o.Extension, ownExtension, StringComparison.OrdinalIgnoreCase))
                .Where(o => q.Length == 0 || Matches(o.FullName, q) || Matches(o.Extension, q))
                .OrderBy(o => o.FullName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public static IReadOnlyList<TransferQueueTarget> FilterQueues(
            TransferTargets? targets,
            string query)
        {
            if (targets == null) return [];
            var q = (query ?? string.Empty).Trim();

            return targets.Queues
                .Where(x => q.Length == 0 || Matches(x.Name, q) || Matches(x.Description, q))
                // Недоступные — вниз, а не прочь: оператор должен увидеть, что
                // очередь есть, и почему она серая.
                .OrderBy(x => string.IsNullOrEmpty(x.DisabledReason) ? 0 : 1)
                .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// i18n-ключ подсказки для недоступной очереди, или null если очередь
        /// доступна. Незнакомая причина не остаётся без текста — иначе
        /// добавленное на бэке значение обернулось бы пустой подсказкой.
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
