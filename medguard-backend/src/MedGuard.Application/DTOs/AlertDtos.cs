using MedGuard.Domain.Enums;

namespace MedGuard.Application.DTOs;

public record AlertDto(
    Guid Id,
    Guid BatchId,
    string BatchNumber,
    string DrugName,
    AlertSeverity Severity,
    string Message,
    DateTime TriggeredAtUtc,
    bool IsResolved,
    DateTime? ResolvedAtUtc
);
