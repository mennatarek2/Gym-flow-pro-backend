namespace GMS.Core.Enums;

/// <summary>
/// Application user roles for RBAC authorization.
/// Ordered by privilege level (highest first).
/// </summary>
public enum UserRole
{
    /// <summary>
    /// Gym owner — full access to all features and settings.
    /// </summary>
    Owner = 1,

    /// <summary>
    /// Gym manager — can manage members, plans, staff. Cannot modify tenant settings.
    /// </summary>
    Manager = 2,

    /// <summary>
    /// Gym trainer — can view members, record attendance, manage schedules.
    /// </summary>
    Trainer = 3,

    /// <summary>
    /// Gym member (mobile app user) — can view own data, check in, invite guests.
    /// </summary>
    Member = 4,

    /// <summary>
    /// Front-desk receptionist — member intake, manual check-in, cash sales. No plan/settings management.
    /// </summary>
    Receptionist = 5
}
