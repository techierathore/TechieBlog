using BlogEngine.Common;
using BlogModels;
using Xunit;

namespace TechieBlog.Tests.Subscribers;

/// <summary>
/// The admin subscriber export (REQ-FN-031, BRD-58): exactly the rows given, real fields, safe in a spreadsheet.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Pins <see cref="SubscriberCsv"/> so an export can neither mislabel a status
/// nor carry a spreadsheet formula.</para>
/// <para><b>Usage:</b> A failure means the CSV header, the status column or the formula guard changed.</para>
/// </remarks>
public class SubscriberCsvTests
{
    private static Subscriber Row(string email, string name, bool active) => new()
    {
        Email = email,
        Name = name,
        SubscribedOn = new DateTime(2026, 10, 27),
        IsActive = active
    };

    /// <summary>
    /// Every row given appears once, in order, with its own status rather than a constant.
    /// </summary>
    [Fact]
    public void BuildWritesOneLinePerSubscriberWithRealStatus()
    {
        // Arrange
        var rows = new[] { Row("a@example.com", "Ann", true), Row("b@example.com", "Bob", false) };

        // Act
        var lines = SubscriberCsv.Build(rows).TrimEnd('\n').Split('\n');

        // Assert
        Assert.Equal(new[]
        {
            "Email,Name,SubscribedOn,Status",
            "\"a@example.com\",\"Ann\",2026-10-27,Active",
            "\"b@example.com\",\"Bob\",2026-10-27,Inactive"
        }, lines);
    }

    /// <summary>
    /// An empty selection exports the header only.
    /// </summary>
    [Fact]
    public void BuildOfNothingIsHeaderOnly()
    {
        // Act
        var text = SubscriberCsv.Build(Array.Empty<Subscriber>());

        // Assert
        Assert.Equal("Email,Name,SubscribedOn,Status\n", text);
    }

    /// <summary>
    /// Embedded quotes are doubled and a leading formula character is neutralised.
    /// </summary>
    [Fact]
    public void BuildEscapesQuotesAndFormulaPrefixes()
    {
        // Arrange
        var rows = new[] { Row("c@example.com", "=HYPERLINK(\"x\")", true), Row("d@example.com", "Say \"hi\"", true) };

        // Act
        var lines = SubscriberCsv.Build(rows).TrimEnd('\n').Split('\n');

        // Assert
        Assert.Equal("\"c@example.com\",\"'=HYPERLINK(\"\"x\"\")\",2026-10-27,Active", lines[1]);
        Assert.Equal("\"d@example.com\",\"Say \"\"hi\"\"\",2026-10-27,Active", lines[2]);
    }
}
