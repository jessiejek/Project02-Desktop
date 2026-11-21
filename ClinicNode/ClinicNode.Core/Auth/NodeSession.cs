// JJQR - AI (Nov 21, 2026) - node-issued session (local secret, not the cloud JWT).
using ClinicApp.Domain.Enums;

namespace ClinicNode.Core.Auth;

public sealed class NodeSession
{
    public required Guid UserId { get; init; }
    public required string Email { get; init; }
    public required string FullName { get; init; }
    public required UserRole Role { get; init; }
    public Guid? StaffId { get; init; }
    public Guid? DoctorId { get; init; }
    public DateTimeOffset IssuedAt { get; init; }
    public DateTimeOffset AccessExpiresAt { get; init; }

    public bool IsDoctor => Role == UserRole.Doctor;
    public bool IsStaff => Role == UserRole.Staff;
    public bool IsAdmin => Role == UserRole.Admin;
    public bool IsStaffLike => IsStaff || IsAdmin;
    public bool IsExpired(DateTimeOffset utcNow) => utcNow >= AccessExpiresAt;
}
// JJQR - END
