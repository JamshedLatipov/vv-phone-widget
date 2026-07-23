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
}
