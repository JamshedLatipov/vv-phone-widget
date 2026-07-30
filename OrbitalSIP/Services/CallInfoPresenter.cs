using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using OrbitalSIP.Models;

namespace OrbitalSIP.Services
{
    /// <summary>One label/value line of a caller-info card.</summary>
    public sealed class CallInfoRowView
    {
        public string Label { get; init; } = string.Empty;
        public string Value { get; init; } = string.Empty;
    }

    /// <summary>
    /// One record inside a section. A `details` section has exactly one; a
    /// `table` section has one per array element (one loan, one account, …).
    /// </summary>
    public sealed class CallInfoRecordView
    {
        /// <summary>Set only when the section holds more than one record, so the
        /// operator can tell the second loan from the first.</summary>
        public string? Heading { get; init; }
        public List<CallInfoRowView> Rows { get; init; } = new();
    }

    public sealed class CallInfoSectionView
    {
        public string Title { get; init; } = string.Empty;
        public List<CallInfoRecordView> Records { get; init; } = new();
    }

    /// <summary>
    /// Turns the `/api/integrations/call-info` payload into flat, render-ready
    /// cards. Kept free of any UI type so the mapping is unit-testable.
    ///
    /// Both section shapes the backend emits are handled (see SectionConfig in
    /// apps/back/.../dto/call-info.dto.ts):
    ///   • `details` — `data` is an object, described by `ui.fields`
    ///   • `table`   — `data` is an ARRAY, described by `ui.columns`
    /// Table sections (Кредиты / Счета / Депозиты) used to be dropped silently
    /// because the widget only ever looked at `ui.fields` and only ever read a
    /// JSON object, so only the `details` sources reached the operator.
    /// </summary>
    public static class CallInfoPresenter
    {
        private static readonly Regex IsoDateTime =
            new(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}", RegexOptions.Compiled);

        private static readonly Regex IsoDate =
            new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled);

        public static List<CallInfoSectionView> BuildSections(CallInfoResponse? response)
        {
            var sections = new List<CallInfoSectionView>();
            if (response?.Sections == null) return sections;

            foreach (var section in response.Sections)
            {
                var view = BuildSection(section);
                if (view != null) sections.Add(view);
            }

            return sections;
        }

        private static CallInfoSectionView? BuildSection(CallInfoSection? section)
        {
            if (section?.Ui == null || section.Data == null) return null;

            var isTable = string.Equals(section.Ui.Type, "table", StringComparison.OrdinalIgnoreCase);
            var descriptors = isTable ? section.Ui.Columns : section.Ui.Fields;

            // Be forgiving about the config: a table without `columns` (or a
            // details section without `fields`) still renders if the other list
            // is filled in.
            if (descriptors == null || descriptors.Count == 0)
                descriptors = isTable ? section.Ui.Fields : section.Ui.Columns;

            if (descriptors == null || descriptors.Count == 0) return null;

            var data = section.Data.Value;
            var records = new List<CallInfoRecordView>();

            if (data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    var rows = BuildRows(item, descriptors);
                    if (rows.Count > 0) records.Add(new CallInfoRecordView { Rows = rows });
                }
            }
            else
            {
                var rows = BuildRows(data, descriptors);
                if (rows.Count > 0) records.Add(new CallInfoRecordView { Rows = rows });
            }

            if (records.Count == 0) return null;

            // Number the records only when there is more than one to number.
            if (records.Count > 1)
            {
                records = records
                    .Select((r, i) => new CallInfoRecordView { Heading = $"#{i + 1}", Rows = r.Rows })
                    .ToList();
            }

            return new CallInfoSectionView
            {
                Title = string.IsNullOrWhiteSpace(section.Ui.Title) ? section.Key : section.Ui.Title,
                Records = records
            };
        }

        private static List<CallInfoRowView> BuildRows(
            JsonElement item,
            List<CallInfoField> descriptors)
        {
            var rows = new List<CallInfoRowView>();

            foreach (var descriptor in descriptors)
            {
                if (descriptor == null || string.IsNullOrEmpty(descriptor.Key)) continue;

                var raw = ResolveRaw(item, descriptor.Key);
                if (raw == null) continue;

                var value = FormatValue(raw, descriptor);
                if (string.IsNullOrWhiteSpace(value)) continue;

                rows.Add(new CallInfoRowView
                {
                    Label = string.IsNullOrWhiteSpace(descriptor.Label) ? descriptor.Key : descriptor.Label,
                    Value = value
                });
            }

            return rows;
        }

        /// <summary>
        /// Reads a value out of one record. The backend flattens nested objects
        /// into literal dotted keys (`"account_status.status": "On"`) but also
        /// keeps the nested object, so try the exact key first and fall back to
        /// walking the dot-notation path. Returns null when absent or empty.
        /// </summary>
        public static string? ResolveRaw(JsonElement item, string key)
        {
            if (item.ValueKind != JsonValueKind.Object || string.IsNullOrEmpty(key))
                return null;

            if (item.TryGetProperty(key, out var direct))
                return ToRawString(direct);

            var current = item;
            foreach (var segment in key.Split('.'))
            {
                if (current.ValueKind != JsonValueKind.Object) return null;
                if (!current.TryGetProperty(segment, out var next)) return null;
                current = next;
            }

            return ToRawString(current);
        }

        private static string? ToRawString(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            // Null / Undefined and whole objects or arrays are not printable
            // values — skip them instead of dumping raw JSON at the operator.
            _ => null
        };

        /// <summary>
        /// Display formatting, mirroring the web card's formatIntegrationValue
        /// (apps/front/.../call-info-card/format-integration-value.ts):
        /// enumMap → declared type → ISO-date heuristic → raw.
        /// </summary>
        public static string FormatValue(string? raw, CallInfoField? descriptor)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;

            if (descriptor?.EnumMap != null &&
                descriptor.EnumMap.TryGetValue(raw, out var mapped))
            {
                return mapped;
            }

            switch (descriptor?.Type?.ToLowerInvariant())
            {
                case "date":
                    return FormatDate(raw, withTime: false);
                case "datetime":
                    return FormatDate(raw, withTime: true);
                case "boolean":
                    return FormatBool(raw) ?? raw;
            }

            if (descriptor?.Type == null)
            {
                var asBool = FormatBool(raw);
                if (asBool != null) return asBool;
                if (IsoDateTime.IsMatch(raw) || IsoDate.IsMatch(raw))
                    return FormatDate(raw, withTime: false);
            }

            return raw;
        }

        private static string? FormatBool(string raw) => raw switch
        {
            "true" or "1" => "Да",
            "false" or "0" => "Нет",
            _ => null
        };

        private static string FormatDate(string raw, bool withTime)
        {
            // A bare `yyyy-MM-dd` carries no time zone: parse it as-is so the
            // calendar day can never shift under time-zone conversion.
            if (IsoDate.IsMatch(raw) &&
                DateTime.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var plainDate))
            {
                return plainDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
            }

            if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal, out var parsed))
            {
                return raw;
            }

            var local = parsed.ToLocalTime();
            return local.ToString(withTime ? "dd.MM.yyyy HH:mm" : "dd.MM.yyyy",
                CultureInfo.InvariantCulture);
        }
    }
}
