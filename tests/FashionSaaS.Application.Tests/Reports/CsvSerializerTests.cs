using FashionSaaS.Application.Reports;
using FluentAssertions;

namespace FashionSaaS.Application.Tests.Reports;

public class CsvSerializerTests
{
    private record Row(string Name, decimal Amount, DateTime When);

    [Fact]
    public void Serialize_WritesHeaderAndRows_InvariantCulture()
    {
        var rows = new[] { new Row("Tee", 1234.5m, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)) };
        var csv = CsvSerializer.Serialize(rows);
        var lines = csv.TrimEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        lines[0].Should().Be("Name,Amount,When");
        lines[1].Should().StartWith("Tee,1234.5,2026-01-02");
    }

    [Fact]
    public void Serialize_QuotesFieldsWithCommasAndQuotes()
    {
        var rows = new[] { new Row("Tee, \"Large\"", 1m, DateTime.UtcNow) };
        var csv = CsvSerializer.Serialize(rows);
        csv.Should().Contain("\"Tee, \"\"Large\"\"\"");
    }

    [Fact]
    public void Serialize_EmptyList_HeaderOnly()
    {
        CsvSerializer.Serialize(Array.Empty<Row>()).TrimEnd().Should().Be("Name,Amount,When");
    }
}
