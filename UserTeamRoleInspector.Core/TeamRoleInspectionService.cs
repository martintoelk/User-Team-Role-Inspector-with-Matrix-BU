using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using UserTeamRoleInspector.Core.Entities;

namespace UserTeamRoleInspector.Core
{
    /// <summary>
    /// Given a systemuserid, returns every security role the user effectively holds - Direct and
    /// Team-Derived (CONTEXT.md) - one row per source. Depends only on IOrganizationService, so
    /// it can be exercised in a unit test against a fake service - no WinForms, no
    /// PluginControlBase/WorkAsync. Uses the early-bound entity classes under Generated/Entities
    /// (regenerate via `pac modelbuilder build` - see issue #3's resolution for the invocation).
    /// </summary>
    public class TeamRoleInspectionService
    {
        private readonly IOrganizationService _service;

        public TeamRoleInspectionService(IOrganizationService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        /// <summary>All users for the picker, ordered by name, with their Home Business Unit and
        /// disabled flag. Not filtered by disabled - the plugin shows all users and flags disabled
        /// ones (see CONTEXT.md / the map's "Not yet specified").</summary>
        public List<UserItem> RetrieveUsers()
        {
            var query = new QueryExpression(SystemUser.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(SystemUser.Fields.FullName, SystemUser.Fields.BusinessUnitId, SystemUser.Fields.IsDisabled),
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
            };
            query.AddOrder(SystemUser.Fields.FullName, OrderType.Ascending);

            var list = new List<UserItem>();
            EntityCollection ec;
            do
            {
                ec = _service.RetrieveMultiple(query);
                foreach (var u in ec.Entities.Select(e => e.ToEntity<SystemUser>()))
                {
                    list.Add(new UserItem
                    {
                        Id = u.Id,
                        Name = u.FullName,
                        BusinessUnitId = u.BusinessUnitId?.Id ?? Guid.Empty,
                        BusinessUnitName = u.BusinessUnitId?.Name ?? string.Empty,
                        IsDisabled = u.IsDisabled ?? false
                    });
                }
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = ec.PagingCookie;
            }
            while (ec.MoreRecords);

            var directCounts = RetrieveDirectRoleCounts();
            var teamCounts = RetrieveTeamDerivedRoleCounts();
            foreach (var u in list)
            {
                directCounts.TryGetValue(u.Id, out var directCount);
                teamCounts.TryGetValue(u.Id, out var teamCount);
                u.DirectCount = directCount;
                u.TeamCount = teamCount;
            }

            return list;
        }

        /// <summary>Direct role count per user, via one bulk <c>systemuserroles</c> query grouped
        /// in memory - a single round trip regardless of user count.</summary>
        private Dictionary<Guid, int> RetrieveDirectRoleCounts()
        {
            var query = new QueryExpression(SystemUserRoles.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(SystemUserRoles.Fields.SystemUserId),
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
            };

            var counts = new Dictionary<Guid, int>();
            EntityCollection ec;
            do
            {
                ec = _service.RetrieveMultiple(query);
                foreach (var r in ec.Entities.Select(e => e.ToEntity<SystemUserRoles>()))
                {
                    if (!r.SystemUserId.HasValue) continue;
                    counts.TryGetValue(r.SystemUserId.Value, out var count);
                    counts[r.SystemUserId.Value] = count + 1;
                }
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = ec.PagingCookie;
            }
            while (ec.MoreRecords);

            return counts;
        }

        /// <summary>Team-Derived role count per user: sum, across each user's teams
        /// (<c>teammembership</c>), of that team's role count (<c>teamroles</c>) - matching
        /// <see cref="GetTeamDerivedAssignments"/>'s row semantics (same role via two teams counts
        /// twice). Two bulk queries total, independent of user or team count.</summary>
        private Dictionary<Guid, int> RetrieveTeamDerivedRoleCounts()
        {
            var teamRoleCounts = RetrieveTeamRoleCounts();

            var userCounts = new Dictionary<Guid, int>();
            var membershipQuery = new QueryExpression(TeamMembership.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(TeamMembership.Fields.SystemUserId, TeamMembership.Fields.TeamId),
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
            };
            EntityCollection ec;
            do
            {
                ec = _service.RetrieveMultiple(membershipQuery);
                foreach (var m in ec.Entities.Select(e => e.ToEntity<TeamMembership>()))
                {
                    if (!m.SystemUserId.HasValue || !m.TeamId.HasValue) continue;
                    teamRoleCounts.TryGetValue(m.TeamId.Value, out var teamRoleCount);
                    userCounts.TryGetValue(m.SystemUserId.Value, out var count);
                    userCounts[m.SystemUserId.Value] = count + teamRoleCount;
                }
                membershipQuery.PageInfo.PageNumber++;
                membershipQuery.PageInfo.PagingCookie = ec.PagingCookie;
            }
            while (ec.MoreRecords);

            return userCounts;
        }

        /// <summary>Role count per team, via one bulk <c>teamroles</c> query grouped in memory -
        /// shared by the user picker's Team-Derived counts and the team picker's Roles count.</summary>
        private Dictionary<Guid, int> RetrieveTeamRoleCounts()
        {
            var query = new QueryExpression(TeamRoles.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(TeamRoles.Fields.TeamId),
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
            };

            var counts = new Dictionary<Guid, int>();
            EntityCollection ec;
            do
            {
                ec = _service.RetrieveMultiple(query);
                foreach (var r in ec.Entities.Select(e => e.ToEntity<TeamRoles>()))
                {
                    if (!r.TeamId.HasValue) continue;
                    counts.TryGetValue(r.TeamId.Value, out var count);
                    counts[r.TeamId.Value] = count + 1;
                }
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = ec.PagingCookie;
            }
            while (ec.MoreRecords);

            return counts;
        }

        /// <summary>Member count per team, via one bulk <c>teammembership</c> query grouped in
        /// memory - the team picker's Members count.</summary>
        private Dictionary<Guid, int> RetrieveTeamMemberCounts()
        {
            var query = new QueryExpression(TeamMembership.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(TeamMembership.Fields.TeamId),
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
            };

            var counts = new Dictionary<Guid, int>();
            EntityCollection ec;
            do
            {
                ec = _service.RetrieveMultiple(query);
                foreach (var m in ec.Entities.Select(e => e.ToEntity<TeamMembership>()))
                {
                    if (!m.TeamId.HasValue) continue;
                    counts.TryGetValue(m.TeamId.Value, out var count);
                    counts[m.TeamId.Value] = count + 1;
                }
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = ec.PagingCookie;
            }
            while (ec.MoreRecords);

            return counts;
        }

        /// <summary>All teams for the picker, ordered by name, with their own Business Unit and
        /// Roles/Members counts (mirrors <see cref="RetrieveUsers"/>'s shape).</summary>
        public List<TeamItem> RetrieveTeams()
        {
            var query = new QueryExpression(Team.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(Team.Fields.Name, Team.Fields.BusinessUnitId),
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
            };
            query.AddOrder(Team.Fields.Name, OrderType.Ascending);

            var list = new List<TeamItem>();
            EntityCollection ec;
            do
            {
                ec = _service.RetrieveMultiple(query);
                foreach (var t in ec.Entities.Select(e => e.ToEntity<Team>()))
                {
                    list.Add(new TeamItem
                    {
                        Id = t.Id,
                        Name = t.Name,
                        BusinessUnitId = t.BusinessUnitId?.Id ?? Guid.Empty,
                        BusinessUnitName = t.BusinessUnitId?.Name ?? string.Empty
                    });
                }
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = ec.PagingCookie;
            }
            while (ec.MoreRecords);

            var roleCounts = RetrieveTeamRoleCounts();
            var memberCounts = RetrieveTeamMemberCounts();
            foreach (var t in list)
            {
                roleCounts.TryGetValue(t.Id, out var roleCount);
                memberCounts.TryGetValue(t.Id, out var memberCount);
                t.RoleCount = roleCount;
                t.MemberCount = memberCount;
            }

            return list;
        }

        /// <summary>Everything the inspector shows for one selected team: its own roles
        /// (<c>teamroles</c>) and its member users (<c>teammembership</c>). The team-mode
        /// counterpart of <see cref="GetAssignments"/>.</summary>
        public TeamDetailResult GetTeamDetail(Guid teamId)
        {
            var columns = new ColumnSet(Team.Fields.Name, Team.Fields.BusinessUnitId);
            var team = _service.Retrieve(Team.EntityLogicalName, teamId, columns).ToEntity<Team>();

            var result = new TeamDetailResult
            {
                TeamId = team.Id,
                TeamName = team.Name,
                BusinessUnitId = team.BusinessUnitId?.Id ?? Guid.Empty,
                BusinessUnitName = team.BusinessUnitId?.Name ?? string.Empty
            };

            result.Roles.AddRange(GetTeamRoles(teamId));
            result.Members.AddRange(GetTeamMembers(teamId));
            return result;
        }

        private List<TeamRoleItem> GetTeamRoles(Guid teamId)
        {
            var query = new QueryExpression(Role.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(Role.Fields.Name, Role.Fields.BusinessUnitId),
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
            };
            var link = query.AddLink(TeamRoles.EntityLogicalName, Role.Fields.RoleId, TeamRoles.Fields.RoleId);
            link.LinkCriteria.AddCondition(TeamRoles.Fields.TeamId, ConditionOperator.Equal, teamId);

            var list = new List<TeamRoleItem>();
            EntityCollection ec;
            do
            {
                ec = _service.RetrieveMultiple(query);
                foreach (var r in ec.Entities.Select(e => e.ToEntity<Role>()))
                {
                    list.Add(new TeamRoleItem
                    {
                        RoleId = r.Id,
                        RoleName = r.Name,
                        RoleBusinessUnitId = r.BusinessUnitId?.Id ?? Guid.Empty,
                        RoleBusinessUnitName = r.BusinessUnitId?.Name ?? string.Empty
                    });
                }
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = ec.PagingCookie;
            }
            while (ec.MoreRecords);

            return list
                .OrderBy(r => r.RoleName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.RoleBusinessUnitName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private List<TeamMemberItem> GetTeamMembers(Guid teamId)
        {
            var query = new QueryExpression(SystemUser.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(SystemUser.Fields.FullName, SystemUser.Fields.IsDisabled),
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
            };
            var link = query.AddLink(TeamMembership.EntityLogicalName, SystemUser.Fields.SystemUserId, TeamMembership.Fields.SystemUserId);
            link.LinkCriteria.AddCondition(TeamMembership.Fields.TeamId, ConditionOperator.Equal, teamId);

            var list = new List<TeamMemberItem>();
            EntityCollection ec;
            do
            {
                ec = _service.RetrieveMultiple(query);
                foreach (var u in ec.Entities.Select(e => e.ToEntity<SystemUser>()))
                {
                    list.Add(new TeamMemberItem
                    {
                        UserId = u.Id,
                        Name = u.FullName,
                        IsDisabled = u.IsDisabled ?? false
                    });
                }
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = ec.PagingCookie;
            }
            while (ec.MoreRecords);

            return list;
        }

        public UserRoleInspectionResult GetAssignments(Guid userId)
        {
            var user = RetrieveUser(userId);

            var result = new UserRoleInspectionResult
            {
                UserId = user.Id,
                UserName = user.FullName,
                IsDisabled = user.IsDisabled ?? false,
                HomeBusinessUnitId = user.BusinessUnitId?.Id ?? Guid.Empty,
                HomeBusinessUnitName = user.BusinessUnitId?.Name ?? string.Empty
            };

            result.Assignments.AddRange(GetDirectAssignments(userId));
            result.Assignments.AddRange(GetTeamDerivedAssignments(userId));
            return result;
        }

        private SystemUser RetrieveUser(Guid userId)
        {
            var columns = new ColumnSet(SystemUser.Fields.FullName, SystemUser.Fields.BusinessUnitId, SystemUser.Fields.IsDisabled);
            return _service.Retrieve(SystemUser.EntityLogicalName, userId, columns).ToEntity<SystemUser>();
        }

        /// <summary>Roles associated straight to the user via systemuserroles, with each role's
        /// own Business Unit (the lookup's Name comes back populated on RetrieveMultiple, no
        /// separate join needed).</summary>
        private List<Assignment> GetDirectAssignments(Guid userId)
        {
            var query = new QueryExpression(Role.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(Role.Fields.Name, Role.Fields.BusinessUnitId),
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
            };
            var link = query.AddLink(SystemUserRoles.EntityLogicalName, Role.Fields.RoleId, SystemUserRoles.Fields.RoleId);
            link.LinkCriteria.AddCondition(SystemUserRoles.Fields.SystemUserId, ConditionOperator.Equal, userId);

            var list = new List<Assignment>();
            EntityCollection ec;
            do
            {
                ec = _service.RetrieveMultiple(query);
                foreach (var r in ec.Entities.Select(e => e.ToEntity<Role>()))
                {
                    list.Add(new Assignment
                    {
                        RoleId = r.Id,
                        RoleName = r.Name,
                        RoleBusinessUnitId = r.BusinessUnitId?.Id ?? Guid.Empty,
                        RoleBusinessUnitName = r.BusinessUnitId?.Name ?? string.Empty,
                        Source = AssignmentSource.Direct
                    });
                }
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = ec.PagingCookie;
            }
            while (ec.MoreRecords);

            return list
                .OrderBy(a => a.RoleName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(a => a.RoleBusinessUnitName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Roles the user effectively holds via team membership: teams the user belongs to
        /// (teammembership), then each such team's roles (teamroles) with the role's own
        /// Business Unit. Two-step - membership then roles - rather than one four-entity join,
        /// mirroring the Assigner's per-entity query style and keeping each step fake-service
        /// testable. No SDK message exposes this directly (see issue #3's resolution).
        /// </summary>
        private List<Assignment> GetTeamDerivedAssignments(Guid userId)
        {
            var teams = RetrieveMemberTeams(userId);
            if (teams.Count == 0)
                return new List<Assignment>();

            var list = new List<Assignment>();
            foreach (var team in teams)
                list.AddRange(RetrieveTeamRoles(team));

            return list
                .OrderBy(a => a.SourceTeamName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(a => a.RoleName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(a => a.RoleBusinessUnitName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private class MemberTeam
        {
            public Guid Id;
            public string Name;
            public Guid BusinessUnitId;
            public string BusinessUnitName;
        }

        /// <summary>Teams the user is a member of, via the teammembership intersect entity, with
        /// each team's own Business Unit.</summary>
        private List<MemberTeam> RetrieveMemberTeams(Guid userId)
        {
            var query = new QueryExpression(TeamMembership.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(false),
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
            };
            query.Criteria.AddCondition(TeamMembership.Fields.SystemUserId, ConditionOperator.Equal, userId);

            var teamLink = query.AddLink(Team.EntityLogicalName, TeamMembership.Fields.TeamId, Team.Fields.TeamId);
            teamLink.EntityAlias = "team";
            teamLink.Columns = new ColumnSet(Team.Fields.TeamId, Team.Fields.Name, Team.Fields.BusinessUnitId);

            var list = new List<MemberTeam>();
            EntityCollection ec;
            do
            {
                ec = _service.RetrieveMultiple(query);
                foreach (var e in ec.Entities)
                {
                    var id = GetAliased<Guid>(e, "team", Team.Fields.TeamId);
                    var name = GetAliased<string>(e, "team", Team.Fields.Name);
                    var bu = GetAliased<EntityReference>(e, "team", Team.Fields.BusinessUnitId);

                    list.Add(new MemberTeam
                    {
                        Id = id,
                        Name = name,
                        BusinessUnitId = bu?.Id ?? Guid.Empty,
                        BusinessUnitName = bu?.Name ?? string.Empty
                    });
                }
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = ec.PagingCookie;
            }
            while (ec.MoreRecords);

            return list;
        }

        private List<Assignment> RetrieveTeamRoles(MemberTeam team)
        {
            var query = new QueryExpression(Role.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(Role.Fields.Name, Role.Fields.BusinessUnitId),
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
            };
            var link = query.AddLink(TeamRoles.EntityLogicalName, Role.Fields.RoleId, TeamRoles.Fields.RoleId);
            link.LinkCriteria.AddCondition(TeamRoles.Fields.TeamId, ConditionOperator.Equal, team.Id);

            var list = new List<Assignment>();
            EntityCollection ec;
            do
            {
                ec = _service.RetrieveMultiple(query);
                foreach (var r in ec.Entities.Select(e => e.ToEntity<Role>()))
                {
                    list.Add(new Assignment
                    {
                        RoleId = r.Id,
                        RoleName = r.Name,
                        RoleBusinessUnitId = r.BusinessUnitId?.Id ?? Guid.Empty,
                        RoleBusinessUnitName = r.BusinessUnitId?.Name ?? string.Empty,
                        Source = AssignmentSource.Team,
                        SourceTeamId = team.Id,
                        SourceTeamName = team.Name,
                        TeamBusinessUnitId = team.BusinessUnitId,
                        TeamBusinessUnitName = team.BusinessUnitName
                    });
                }
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = ec.PagingCookie;
            }
            while (ec.MoreRecords);

            return list;
        }

        private static T GetAliased<T>(Entity entity, string alias, string attribute)
        {
            var key = alias + "." + attribute;
            if (!entity.Contains(key))
                return default(T);
            return ((AliasedValue)entity[key]).Value is T value ? value : default(T);
        }
    }
}
