using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OrbitalSIP.Models
{
    /// <summary>
    /// Payload for POST /api/tasks. Field names mirror the backend CreateTaskDto.
    /// Null fields are omitted on the wire (see TaskService), so optional links
    /// (task type, due date, call anchor) only appear when actually set.
    /// </summary>
    public class CreateTaskRequest
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("priority")]
        public string? Priority { get; set; }

        /// <summary>ISO-8601 date string, or null for no deadline.</summary>
        [JsonPropertyName("dueDate")]
        public string? DueDate { get; set; }

        [JsonPropertyName("taskTypeId")]
        public int? TaskTypeId { get; set; }

        /// <summary>Operator the task is assigned to (numeric user id from the JWT).</summary>
        [JsonPropertyName("assignedToId")]
        public int? AssignedToId { get; set; }

        /// <summary>
        /// UUID of the CallLog row this task is created from. This is the field the CRM
        /// reads to show the linked call on a task (task-modal / task-detail / board card).
        /// Obtained from POST /api/cdr/log's response id — NOT the raw Asterisk uniqueId.
        /// </summary>
        [JsonPropertyName("callLogId")]
        public string? CallLogId { get; set; }
    }

    /// <summary>A row from GET /api/task-types.</summary>
    public class TaskTypeItem
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("color")]
        public string? Color { get; set; }

        [JsonPropertyName("sortOrder")]
        public int SortOrder { get; set; }

        [JsonPropertyName("isActive")]
        public bool IsActive { get; set; } = true;
    }

    /// <summary>A row from GET /api/tasks.</summary>
    public class TaskItem
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        /// <summary>pending, in_progress, done, overdue — or null on older rows.</summary>
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        /// <summary>low, medium, high, urgent — or null.</summary>
        [JsonPropertyName("priority")]
        public string? Priority { get; set; }

        [JsonPropertyName("dueDate")]
        public DateTimeOffset? DueDate { get; set; }

        [JsonPropertyName("taskType")]
        public TaskTypeItem? TaskType { get; set; }

        [JsonPropertyName("createdAt")]
        public DateTimeOffset? CreatedAt { get; set; }
    }

    /// <summary>The envelope GET /api/tasks answers with.</summary>
    public class TaskListResponse
    {
        /// <summary>
        /// Elements are nullable because the backend has been seen to send
        /// <c>"data": [null]</c>, and both call sites filter for it. Declaring them
        /// non-null promised callers something the runtime handling right beside it does
        /// not believe — the same claim OpenTaskList.From refused to make.
        /// </summary>
        [JsonPropertyName("data")]
        public List<TaskItem?> Data { get; set; } = new();

        [JsonPropertyName("total")]
        public int Total { get; set; }
    }

    /// <summary>GET /api/tasks/stats.</summary>
    public class TaskStats
    {
        [JsonPropertyName("total")]
        public int Total { get; set; }

        [JsonPropertyName("pending")]
        public int Pending { get; set; }

        [JsonPropertyName("inProgress")]
        public int InProgress { get; set; }

        [JsonPropertyName("done")]
        public int Done { get; set; }

        [JsonPropertyName("overdue")]
        public int Overdue { get; set; }
    }
}
