using MDTPro.Logic;
using Xunit;

namespace MDTProPlugin.Logic.Tests;

public sealed class DocumentAuthorityRulesTests
{
    [Fact]
    public void ComposeStpLicenseExpiry_UsesBirthdayMonthDayAndCdfYear()
    {
        var result = DocumentAuthorityRules.ComposeStpLicenseExpiry("1990-09-15", "2030-01-01");
        Assert.Equal("2030-09-15", result);
    }

    [Fact]
    public void ComposeStpLicenseExpiry_LeapDayClampsForNonLeapYear()
    {
        var result = DocumentAuthorityRules.ComposeStpLicenseExpiry("1992-02-29", "2031-12-31");
        Assert.Equal("2031-02-28", result);
    }

    [Theory]
    [InlineData(null, "2030-01-01")]
    [InlineData("1990-09-15", null)]
    [InlineData("1990-09-15", "Error")]
    public void ComposeStpLicenseExpiry_ReturnsNullWhenCdfYearMissing(string? birthday, string? cdfExpiration)
    {
        Assert.Null(DocumentAuthorityRules.ComposeStpLicenseExpiry(birthday!, cdfExpiration!));
    }

    [Fact]
    public void ShouldEmitVehicleDocumentExpiration_FalseWhenNotVerified()
    {
        Assert.False(DocumentAuthorityRules.ShouldEmitVehicleDocumentExpiration("Valid", "2030-01-01", verifiedFromLiveDocument: false));
        Assert.True(DocumentAuthorityRules.ShouldEmitVehicleDocumentExpiration("Valid", "2030-01-01", verifiedFromLiveDocument: true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Unknown")]
    [InlineData("Error")]
    [InlineData("Missing")]
    [InlineData("N/A")]
    [InlineData("na")]
    public void ShouldEmitVehicleDocumentExpiration_FalseForWeakStatuses(string? status)
    {
        Assert.False(DocumentAuthorityRules.ShouldEmitVehicleDocumentExpiration(status, "2030-01-01", verifiedFromLiveDocument: true));
    }

    [Fact]
    public void ShouldEmitVehicleDocumentExpiration_TreatsNoneAsStrongStatus()
    {
        Assert.True(DocumentAuthorityRules.ShouldEmitVehicleDocumentExpiration("None", "2030-01-01", verifiedFromLiveDocument: true));
    }

    [Fact]
    public void ReconcileStpVehicleExpiration_ClearsValidFutureDateWhenStpStatusExpired()
    {
        var future = DateTime.UtcNow.AddYears(3).ToString("yyyy-MM-dd");
        Assert.Null(DocumentAuthorityRules.ReconcileStpVehicleExpiration("Expired", future, currentVerified: true));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("Unknown", true)]
    [InlineData("Error", true)]
    [InlineData("Missing", true)]
    [InlineData("None", false)]
    [InlineData("Valid", false)]
    [InlineData("Expired", false)]
    public void IsWeakVehicleDocumentStatus_TreatsUnknownErrorMissingAsWeakButNoneAsStrong(string? status, bool expected)
    {
        Assert.Equal(expected, DocumentAuthorityRules.IsWeakVehicleDocumentStatus(status!));
    }
}
