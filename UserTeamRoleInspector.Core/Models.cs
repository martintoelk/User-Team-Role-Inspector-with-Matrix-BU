using System;
using System.Collections.Generic;

namespace UserTeamRoleInspector.Core
{
    /// <summary>Which path an Assignment came through. See CONTEXT.md's "Source".</summary>
    public enum AssignmentSource
    {
        Direct,
        Team
    }

    /// <summary>
    /// One row in the inspector's results: a Direct Assignment or a Team-Derived Assignment
    /// (CONTEXT.md). Two Assignments are distinct even if they name the same Role, as long as
    /// their source differs.
    /// </summary>
    public class Assignment
    {
        public Guid RoleId { get; set; }
        public string RoleName { get; set; }

        /// <summary>The business unit that owns this specific role instance, independent of the
        /// user's or team's own business unit under the modernized model.</summary>
        public Guid RoleBusinessUnitId { get; set; }
        public string RoleBusinessUnitName { get; set; }

        public AssignmentSource Source { get; set; }

        /// <summary>Null for Direct; the team the role was derived through for Team-Derived.</summary>
        public Guid? SourceTeamId { get; set; }
        public string SourceTeamName { get; set; }

        /// <summary>Null for Direct; the source team's own business unit for Team-Derived -
        /// a distinct axis from <see cref="RoleBusinessUnitId"/>.</summary>
        public Guid? TeamBusinessUnitId { get; set; }
        public string TeamBusinessUnitName { get; set; }

        /// <summary>"Direct" or the source team's name, for the grid's Source column.</summary>
        public string SourceLabel => Source == AssignmentSource.Direct ? "Direct" : SourceTeamName;
    }

    /// <summary>Everything the inspector shows for one selected user.</summary>
    public class UserRoleInspectionResult
    {
        public Guid UserId { get; set; }
        public string UserName { get; set; }
        public bool IsDisabled { get; set; }

        /// <summary>The user's own business unit (systemuser.businessunitid) - context only,
        /// distinct from any Role Business Unit or Team Business Unit in the results.</summary>
        public Guid HomeBusinessUnitId { get; set; }
        public string HomeBusinessUnitName { get; set; }

        public List<Assignment> Assignments { get; set; } = new List<Assignment>();
    }

    /// <summary>One row in the user picker (CONTEXT.md's User Home Business Unit).</summary>
    public class UserItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public Guid BusinessUnitId { get; set; }
        public string BusinessUnitName { get; set; }
        public bool IsDisabled { get; set; }

        /// <summary>Row counts matching what <see cref="TeamRoleInspectionService.GetAssignments"/>
        /// would return for this user (same role via two teams counts as two Team-Derived rows).</summary>
        public int DirectCount { get; set; }
        public int TeamCount { get; set; }
    }
}
