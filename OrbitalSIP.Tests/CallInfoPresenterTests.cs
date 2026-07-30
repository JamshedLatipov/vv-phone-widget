using System.Text.Json;
using OrbitalSIP.Models;
using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests
{
    /// <summary>
    /// Guards the caller-card mapping for BOTH section shapes the backend emits.
    /// The regression these cover: `table` sections (Кредиты / Счета / Депозиты)
    /// are described by `ui.columns` and carry an ARRAY in `data`, and used to be
    /// dropped by the widget, so the operator only ever saw the `details` sources.
    /// </summary>
    public class CallInfoPresenterTests
    {
        private static readonly JsonSerializerOptions ReadOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static CallInfoResponse Parse(string json) =>
            JsonSerializer.Deserialize<CallInfoResponse>(json, ReadOptions)!;

        [Fact]
        public void TableSection_RendersOneRecordPerArrayItem()
        {
            var response = Parse("""
            {
              "sections": [
                {
                  "key": "loan",
                  "ui": {
                    "type": "table",
                    "title": "Кредиты",
                    "columns": [
                      { "key": "contractNo", "label": "Договор" },
                      { "key": "totalDebtAmount", "label": "Остаток долга" },
                      { "key": "loanStatusForBr", "label": "Статус" }
                    ]
                  },
                  "data": [
                    { "contractNo": "TJ-1", "totalDebtAmount": 1500.5, "loanStatusForBr": "Активный" },
                    { "contractNo": "TJ-2", "totalDebtAmount": 200, "loanStatusForBr": "Активный" }
                  ]
                }
              ]
            }
            """);

            var sections = CallInfoPresenter.BuildSections(response);

            var loans = Assert.Single(sections);
            Assert.Equal("Кредиты", loans.Title);
            Assert.Equal(2, loans.Records.Count);

            Assert.Equal("#1", loans.Records[0].Heading);
            Assert.Equal("#2", loans.Records[1].Heading);

            Assert.Equal(
                new[] { ("Договор", "TJ-1"), ("Остаток долга", "1500.5"), ("Статус", "Активный") },
                loans.Records[0].Rows.Select(r => (r.Label, r.Value)));
            Assert.Equal("TJ-2", loans.Records[1].Rows[0].Value);
        }

        [Fact]
        public void SingleRecordTable_IsNotNumbered()
        {
            var response = Parse("""
            {
              "sections": [
                {
                  "key": "account",
                  "ui": {
                    "type": "table",
                    "title": "Счета",
                    "columns": [ { "key": "iban", "label": "Счёт (IBAN)" } ]
                  },
                  "data": [ { "iban": "TJ01" } ]
                }
              ]
            }
            """);

            var section = Assert.Single(CallInfoPresenter.BuildSections(response));
            Assert.Null(Assert.Single(section.Records).Heading);
        }

        [Fact]
        public void DetailsSection_StillRenders_WithDottedAndNestedKeys()
        {
            var response = Parse("""
            {
              "sections": [
                {
                  "key": "customer",
                  "ui": {
                    "type": "details",
                    "title": "Клиент",
                    "fields": [
                      { "key": "fio", "label": "ФИО" },
                      { "key": "account_status.status", "label": "Статус аккаунта" },
                      { "key": "tarif.name", "label": "Тариф" }
                    ]
                  },
                  "data": {
                    "fio": "Иванов Иван",
                    "account_status.status": "On",
                    "tarif": { "name": "Оилаи Чавон" }
                  }
                }
              ]
            }
            """);

            var section = Assert.Single(CallInfoPresenter.BuildSections(response));
            var rows = Assert.Single(section.Records).Rows;

            Assert.Equal("Иванов Иван", rows[0].Value);
            Assert.Equal("On", rows[1].Value);       // literal dotted key
            Assert.Equal("Оилаи Чавон", rows[2].Value); // nested path fallback
        }

        [Fact]
        public void EmptyArray_YieldsNoSection()
        {
            var response = Parse("""
            {
              "sections": [
                {
                  "key": "deposit",
                  "ui": {
                    "type": "table",
                    "title": "Депозиты",
                    "columns": [ { "key": "contractNo", "label": "Договор" } ]
                  },
                  "data": []
                }
              ]
            }
            """);

            Assert.Empty(CallInfoPresenter.BuildSections(response));
        }

        [Fact]
        public void MissingColumnValues_AreSkipped_NotRenderedAsBlanks()
        {
            var response = Parse("""
            {
              "sections": [
                {
                  "key": "loan",
                  "ui": {
                    "type": "table",
                    "title": "Кредиты",
                    "columns": [
                      { "key": "contractNo", "label": "Договор" },
                      { "key": "nextPaymentAmount", "label": "След. платёж" }
                    ]
                  },
                  "data": [ { "contractNo": "TJ-1", "nextPaymentAmount": null } ]
                }
              ]
            }
            """);

            var rows = Assert.Single(Assert.Single(CallInfoPresenter.BuildSections(response)).Records).Rows;
            Assert.Equal("Договор", Assert.Single(rows).Label);
        }

        [Fact]
        public void ObjectValuedCell_IsSkipped_InsteadOfDumpingJson()
        {
            var response = Parse("""
            {
              "sections": [
                {
                  "key": "loan",
                  "ui": {
                    "type": "table",
                    "title": "Кредиты",
                    "columns": [
                      { "key": "contractNo", "label": "Договор" },
                      { "key": "schedule", "label": "График" }
                    ]
                  },
                  "data": [ { "contractNo": "TJ-1", "schedule": { "items": [1, 2] } } ]
                }
              ]
            }
            """);

            var rows = Assert.Single(Assert.Single(CallInfoPresenter.BuildSections(response)).Records).Rows;
            Assert.Equal("Договор", Assert.Single(rows).Label);
        }

        [Fact]
        public void TableWithoutColumns_FallsBackToFields()
        {
            var response = Parse("""
            {
              "sections": [
                {
                  "key": "loan",
                  "ui": {
                    "type": "table",
                    "title": "Кредиты",
                    "fields": [ { "key": "contractNo", "label": "Договор" } ]
                  },
                  "data": [ { "contractNo": "TJ-1" } ]
                }
              ]
            }
            """);

            var rows = Assert.Single(Assert.Single(CallInfoPresenter.BuildSections(response)).Records).Rows;
            Assert.Equal("TJ-1", Assert.Single(rows).Value);
        }

        [Fact]
        public void DetailsSection_AcceptsArrayData_UsingEveryItem()
        {
            var response = Parse("""
            {
              "sections": [
                {
                  "key": "customer",
                  "ui": {
                    "type": "details",
                    "title": "Клиент",
                    "fields": [ { "key": "fio", "label": "ФИО" } ]
                  },
                  "data": [ { "fio": "Иванов Иван" } ]
                }
              ]
            }
            """);

            var section = Assert.Single(CallInfoPresenter.BuildSections(response));
            Assert.Equal("Иванов Иван", Assert.Single(Assert.Single(section.Records).Rows).Value);
        }

        [Fact]
        public void SectionWithoutTitle_FallsBackToKey()
        {
            var response = Parse("""
            {
              "sections": [
                {
                  "key": "wallet",
                  "ui": { "type": "details", "fields": [ { "key": "status", "label": "Статус" } ] },
                  "data": { "status": "Ok" }
                }
              ]
            }
            """);

            Assert.Equal("wallet", Assert.Single(CallInfoPresenter.BuildSections(response)).Title);
        }

        [Fact]
        public void EnumMap_TranslatesRawCode()
        {
            var field = new CallInfoField
            {
                Key = "loanStatusForBr",
                Label = "Статус",
                Type = "enum",
                EnumMap = new() { ["A"] = "Активный", ["C"] = "Закрыт" }
            };

            Assert.Equal("Активный", CallInfoPresenter.FormatValue("A", field));
            Assert.Equal("Закрыт", CallInfoPresenter.FormatValue("C", field));
            Assert.Equal("X", CallInfoPresenter.FormatValue("X", field)); // unmapped → raw
        }

        [Theory]
        [InlineData("2026-07-30", null, "30.07.2026")]
        [InlineData("2026-07-30", "date", "30.07.2026")]
        [InlineData("2024-05-07T00:00:00+05:00", null, "07.05.2024")]
        [InlineData("не дата", "date", "не дата")]
        public void IsoDates_AreFormattedForTheOperator(string raw, string? type, string expected)
        {
            var field = new CallInfoField { Key = "d", Label = "Дата", Type = type };
            Assert.Equal(expected, CallInfoPresenter.FormatValue(raw, field));
        }

        [Fact]
        public void Booleans_RenderInRussian()
        {
            var untyped = new CallInfoField { Key = "b", Label = "Флаг" };
            var typed = new CallInfoField { Key = "b", Label = "Флаг", Type = "boolean" };

            Assert.Equal("Да", CallInfoPresenter.FormatValue("true", untyped));
            Assert.Equal("Нет", CallInfoPresenter.FormatValue("false", untyped));
            Assert.Equal("Да", CallInfoPresenter.FormatValue("1", typed));
        }

        [Fact]
        public void NullResponseOrSections_YieldEmptyList()
        {
            Assert.Empty(CallInfoPresenter.BuildSections(null));
            Assert.Empty(CallInfoPresenter.BuildSections(new CallInfoResponse()));
        }
    }
}
