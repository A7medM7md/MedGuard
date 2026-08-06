using MedGuard.Domain.Enums;

namespace MedGuard.Application.DTOs;

public record AlertDto(
    Guid Id,
    Guid BatchId,
    AlertSeverity Severity,
    string Message,
    DateTime TriggeredAtUtc,
    bool IsResolved,
    DateTime? ResolvedAtUtc
);
