using System.Text.Json;
using System.Text.Json.Serialization;
using OrbitalSIP.Models;
using Xunit;

namespace OrbitalSIP.Tests
{
    /// <summary>
    /// Guards the wire contract for POST /api/tasks: property names must match the
    /// backend CreateTaskDto, and null fields must be dropped so class-validator's
    /// @IsOptional/@IsUUID checks don't trip on empty links. Mirrors TaskService's
    /// serialization options.
    /// </summary>
    public class TaskModelsTests
    {
        private static readonly JsonSerializerOptions WriteOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        [Fact]
        public void CreateTaskRequest_UsesBackendFieldNames()
        {
            var request = new CreateTaskRequest
            {
                Title = "Перезвонить 100",
                Description = "Обсудить тариф",
                Priority = "high",
                DueDate = "2026-07-23T10:00:00.0000000+05:00",
                TaskTypeId = 3,
                AssignedToId = 42,
                CallLogId = "3f6a2c1e-0b7d-4a9c-8e21-2b9f0d5c6a11",
            };

            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(request, WriteOptions));
            var root = doc.RootElement;

            Assert.Equal("Перезвонить 100", root.GetProperty("title").GetString());
            Assert.Equal("Обсудить тариф", root.GetProperty("description").GetString());
            Assert.Equal("high", root.GetProperty("priority").GetString());
            Assert.Equal("2026-07-23T10:00:00.0000000+05:00", root.GetProperty("dueDate").GetString());
            Assert.Equal(3, root.GetProperty("taskTypeId").GetInt32());
            Assert.Equal(42, root.GetProperty("assignedToId").GetInt32());
            Assert.Equal("3f6a2c1e-0b7d-4a9c-8e21-2b9f0d5c6a11", root.GetProperty("callLogId").GetString());
        }

        [Fact]
        public void CreateTaskRequest_OmitsNullFields()
        {
            var request = new CreateTaskRequest { Title = "Только заголовок" };

            var json = JsonSerializer.Serialize(request, WriteOptions);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal("Только заголовок", root.GetProperty("title").GetString());
            Assert.False(root.TryGetProperty("description", out _));
            Assert.False(root.TryGetProperty("priority", out _));
            Assert.False(root.TryGetProperty("dueDate", out _));
            Assert.False(root.TryGetProperty("taskTypeId", out _));
            Assert.False(root.TryGetProperty("assignedToId", out _));
            Assert.False(root.TryGetProperty("callLogId", out _));
        }

        [Fact]
        public void TaskTypeItem_DeserializesCamelCaseJson()
        {
            const string json = "{\"id\":5,\"name\":\"Звонок\",\"color\":\"#3B82F6\",\"sortOrder\":2,\"isActive\":true}";

            var item = JsonSerializer.Deserialize<TaskTypeItem>(
                json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            Assert.NotNull(item);
            Assert.Equal(5, item!.Id);
            Assert.Equal("Звонок", item.Name);
            Assert.Equal("#3B82F6", item.Color);
            Assert.Equal(2, item.SortOrder);
            Assert.True(item.IsActive);
        }
    }
}
