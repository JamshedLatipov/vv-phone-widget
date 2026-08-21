using System;
using OrbitalSIP.Models;
using OrbitalSIP.Services;

namespace OrbitalSIP.ViewModels
{
    /// <summary>
    /// One row of the tasks list, pre-computed for binding.
    ///
    /// Same division of labour as CdrItemViewModel: everything the XAML needs is a plain
    /// property here, so the markup stays free of converters. The arithmetic itself lives
    /// in TaskItemPresenter, which is where the tests can reach it.
    /// </summary>
    public class TaskItemViewModel
    {
        public TaskItem Task { get; }

        public int Id => Task.Id;
        public string Title { get; }
        public string Subtitle { get; }
        public string PriorityColor { get; }
        public string SubtitleColor { get; }

        public TaskItemViewModel(TaskItem task, DateTimeOffset now)
        {
            Task = task;

            Title = string.IsNullOrWhiteSpace(task.Title) ? "—" : task.Title.Trim();
            PriorityColor = TaskItemPresenter.PriorityColor(task.Priority);

            var i18n = I18nService.Instance;
            var bucket = TaskItemPresenter.Bucket(task, now);
            var time = TaskItemPresenter.TimeText(task.DueDate, now);

            // TaskDue*, not Due*: the i18n files already carry a Due* family — DueNone,
            // DueIn30m, DueIn1h, DueTomorrow — and those are TaskDialog's deadline
            // *presets* ("Завтра, 09:00"), not the words a row label reads with. A second
            // "DueTomorrow" would not have failed: I18nService deserialises into a
            // Dictionary<string, string>, where a repeated key silently takes the last
            // value, so the picker would quietly have lost the 09:00 that tells the
            // operator what the preset actually sets.
            var due = bucket switch
            {
                DueBucket.Overdue  => $"{i18n.Get("TaskDueOverdue")} {time}".Trim(),
                DueBucket.Today    => $"{i18n.Get("TaskDueToday")} {time}".Trim(),
                DueBucket.Tomorrow => $"{i18n.Get("TaskDueTomorrow")} {time}".Trim(),
                DueBucket.Later    => time,
                _                  => string.Empty,
            };

            var type = task.TaskType?.Name?.Trim() ?? string.Empty;

            Subtitle = (type.Length, due.Length) switch
            {
                (> 0, > 0) => $"{type} · {due}",
                (> 0, _)   => type,
                (_, > 0)   => due,
                _          => string.Empty,
            };

            // The deadline is the only part of the subtitle worth alarming about, so a row
            // with no deadline keeps the quiet colour even when it is the only text there.
            SubtitleColor = bucket == DueBucket.Overdue ? "#FCA5A5" : "#6E859D";
        }
    }
}
