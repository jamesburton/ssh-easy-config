namespace SshEasyConfig.Diagnostics;

public enum CheckStatus { Pass, Warn, Fail, Skip }

public record DiagnosticResult(
    string CheckName,
    CheckStatus Status,
    string Message,
    string? FixSuggestion = null,
    bool AutoFixAvailable = false,
    Func<Task>? FixAction = null);
