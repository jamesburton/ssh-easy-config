using SshEasyConfig.Core;

namespace SshEasyConfig.Tests.Core;

public class SshdConfigManagerTests
{
    [Fact]
    public void Audit_DetectsPasswordAuthEnabled()
    {
        var content = "PasswordAuthentication yes\nPubkeyAuthentication yes\n";
        var findings = SshdConfigManager.Audit(content);
        Assert.Contains(findings, f => f.Key == "PasswordAuthentication" && f.Severity == AuditSeverity.Warning);
    }

    [Fact]
    public void Audit_DetectsMissingPubkeyAuth()
    {
        var content = "PasswordAuthentication no\n";
        var findings = SshdConfigManager.Audit(content);
        Assert.Contains(findings, f => f.Key == "PubkeyAuthentication");
    }

    [Fact]
    public void Audit_DetectsRootLoginPermitted()
    {
        var content = "PermitRootLogin yes\nPasswordAuthentication no\nPubkeyAuthentication yes\n";
        var findings = SshdConfigManager.Audit(content);
        Assert.Contains(findings, f => f.Key == "PermitRootLogin" && f.Severity == AuditSeverity.Warning);
    }

    [Fact]
    public void Audit_PassesSecureConfig()
    {
        var content = "PasswordAuthentication no\nPubkeyAuthentication yes\nPermitRootLogin no\n";
        var findings = SshdConfigManager.Audit(content);
        Assert.All(findings, f => Assert.Equal(AuditSeverity.Ok, f.Severity));
    }

    [Fact]
    public void SetDirective_UpdatesExistingValue()
    {
        var content = "PasswordAuthentication yes\nPubkeyAuthentication no\n";
        var result = SshdConfigManager.SetDirective(content, "PasswordAuthentication", "no");
        Assert.Contains("PasswordAuthentication no", result);
        Assert.DoesNotContain("PasswordAuthentication yes", result);
    }

    [Fact]
    public void SetDirective_AddsNewDirective()
    {
        var content = "PubkeyAuthentication yes\n";
        var result = SshdConfigManager.SetDirective(content, "PasswordAuthentication", "no");
        Assert.Contains("PasswordAuthentication no", result);
        Assert.Contains("PubkeyAuthentication yes", result);
    }

    [Fact]
    public void SetDirective_PreservesComments()
    {
        var content = "# Important comment\nPasswordAuthentication yes\n";
        var result = SshdConfigManager.SetDirective(content, "PasswordAuthentication", "no");
        Assert.Contains("# Important comment", result);
    }
}
