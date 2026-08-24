using System;
using System.Linq;
using Xunit;

namespace UserTeamRoleInspector.Core.Tests
{
    public class TeamRoleInspectionServiceTests
    {
        private static readonly Guid RootBuId = Guid.NewGuid();

        [Fact]
        public void GetAssignments_UserWithOnlyDirectRoles_ReturnsOneDirectAssignmentPerRole()
        {
            var fake = new FakeOrganizationService();
            var user = fake.SeedUser(Guid.NewGuid(), "Alice", RootBuId, "Root BU");
            var role = fake.SeedRole(Guid.NewGuid(), "Salesperson", RootBuId, "Root BU");
            fake.SeedUserRole(user.Id, role.Id);
            var sut = new TeamRoleInspectionService(fake);

            var result = sut.GetAssignments(user.Id);

            var assignment = Assert.Single(result.Assignments);
            Assert.Equal(AssignmentSource.Direct, assignment.Source);
            Assert.Equal(role.Id, assignment.RoleId);
            Assert.Equal("Salesperson", assignment.RoleName);
            Assert.Equal(RootBuId, assignment.RoleBusinessUnitId);
            Assert.Null(assignment.SourceTeamId);
            Assert.Equal("Direct", assignment.SourceLabel);
        }

        [Fact]
        public void GetAssignments_UserWithOnlyTeamDerivedRoles_ReturnsAssignmentWithSourceTeamAndTeamBu()
        {
            var fake = new FakeOrganizationService();
            var user = fake.SeedUser(Guid.NewGuid(), "Bob", RootBuId, "Root BU");
            var teamBuId = Guid.NewGuid();
            var team = fake.SeedTeam(Guid.NewGuid(), "Sales Team", teamBuId, "Team BU");
            var role = fake.SeedRole(Guid.NewGuid(), "Salesperson", RootBuId, "Root BU");
            fake.SeedTeamMembership(user.Id, team.Id);
            fake.SeedTeamRole(team.Id, role.Id);
            var sut = new TeamRoleInspectionService(fake);

            var result = sut.GetAssignments(user.Id);

            var assignment = Assert.Single(result.Assignments);
            Assert.Equal(AssignmentSource.Team, assignment.Source);
            Assert.Equal(role.Id, assignment.RoleId);
            Assert.Equal(team.Id, assignment.SourceTeamId);
            Assert.Equal("Sales Team", assignment.SourceTeamName);
            Assert.Equal(teamBuId, assignment.TeamBusinessUnitId);
            Assert.Equal("Team BU", assignment.TeamBusinessUnitName);
            Assert.Equal("Sales Team", assignment.SourceLabel);
        }

        [Fact]
        public void GetAssignments_UserWithBothDirectAndTeamDerivedRoles_ReturnsBoth()
        {
            var fake = new FakeOrganizationService();
            var user = fake.SeedUser(Guid.NewGuid(), "Carol", RootBuId, "Root BU");
            var directRole = fake.SeedRole(Guid.NewGuid(), "Marketing", RootBuId, "Root BU");
            fake.SeedUserRole(user.Id, directRole.Id);
            var team = fake.SeedTeam(Guid.NewGuid(), "Sales Team", RootBuId, "Root BU");
            var teamRole = fake.SeedRole(Guid.NewGuid(), "Salesperson", RootBuId, "Root BU");
            fake.SeedTeamMembership(user.Id, team.Id);
            fake.SeedTeamRole(team.Id, teamRole.Id);
            var sut = new TeamRoleInspectionService(fake);

            var result = sut.GetAssignments(user.Id);

            Assert.Equal(2, result.Assignments.Count);
            Assert.Contains(result.Assignments, a => a.Source == AssignmentSource.Direct && a.RoleId == directRole.Id);
            Assert.Contains(result.Assignments, a => a.Source == AssignmentSource.Team && a.RoleId == teamRole.Id);
        }

        [Fact]
        public void GetAssignments_SameRoleViaTwoDifferentTeams_ReturnsTwoDistinctRows()
        {
            // CONTEXT.md: two Assignments are distinct as long as their Source differs, even for
            // the same Role - no collapsing across teams.
            var fake = new FakeOrganizationService();
            var user = fake.SeedUser(Guid.NewGuid(), "Dave", RootBuId, "Root BU");
            var role = fake.SeedRole(Guid.NewGuid(), "Salesperson", RootBuId, "Root BU");
            var teamA = fake.SeedTeam(Guid.NewGuid(), "Team A", RootBuId, "Root BU");
            var teamB = fake.SeedTeam(Guid.NewGuid(), "Team B", RootBuId, "Root BU");
            fake.SeedTeamMembership(user.Id, teamA.Id);
            fake.SeedTeamMembership(user.Id, teamB.Id);
            fake.SeedTeamRole(teamA.Id, role.Id);
            fake.SeedTeamRole(teamB.Id, role.Id);
            var sut = new TeamRoleInspectionService(fake);

            var result = sut.GetAssignments(user.Id);

            Assert.Equal(2, result.Assignments.Count);
            Assert.All(result.Assignments, a => Assert.Equal(role.Id, a.RoleId));
            Assert.Contains(result.Assignments, a => a.SourceTeamId == teamA.Id);
            Assert.Contains(result.Assignments, a => a.SourceTeamId == teamB.Id);
        }

        [Fact]
        public void GetAssignments_MultipleTeamDerivedRoles_AreOrderedByTeamThenRoleThenRoleBu()
        {
            var fake = new FakeOrganizationService();
            var user = fake.SeedUser(Guid.NewGuid(), "Kim", RootBuId, "Root BU");

            var teamZ = fake.SeedTeam(Guid.NewGuid(), "Zulu Team", RootBuId, "Root BU");
            var teamA = fake.SeedTeam(Guid.NewGuid(), "Alpha Team", RootBuId, "Root BU");
            fake.SeedTeamMembership(user.Id, teamZ.Id);
            fake.SeedTeamMembership(user.Id, teamA.Id);

            var roleB = fake.SeedRole(Guid.NewGuid(), "Bravo Role", RootBuId, "Root BU");
            var roleA = fake.SeedRole(Guid.NewGuid(), "Alpha Role", RootBuId, "Root BU");
            var buZ = Guid.NewGuid();
            var roleAInOtherBu = fake.SeedRole(Guid.NewGuid(), "Alpha Role", buZ, "Zulu BU");

            fake.SeedTeamRole(teamA.Id, roleB.Id);
            fake.SeedTeamRole(teamA.Id, roleA.Id);
            fake.SeedTeamRole(teamA.Id, roleAInOtherBu.Id);
            fake.SeedTeamRole(teamZ.Id, roleA.Id);

            var sut = new TeamRoleInspectionService(fake);

            var result = sut.GetAssignments(user.Id);

            var ordered = result.Assignments.Select(a => (a.SourceTeamName, a.RoleName, a.RoleBusinessUnitName)).ToList();
            Assert.Equal(new[]
            {
                ("Alpha Team", "Alpha Role", "Root BU"),
                ("Alpha Team", "Alpha Role", "Zulu BU"),
                ("Alpha Team", "Bravo Role", "Root BU"),
                ("Zulu Team", "Alpha Role", "Root BU")
            }, ordered);
        }

        [Fact]
        public void GetAssignments_MultipleDirectRoles_AreOrderedByRoleThenRoleBu()
        {
            var fake = new FakeOrganizationService();
            var user = fake.SeedUser(Guid.NewGuid(), "Liam", RootBuId, "Root BU");

            var roleB = fake.SeedRole(Guid.NewGuid(), "Bravo Role", RootBuId, "Root BU");
            var roleA = fake.SeedRole(Guid.NewGuid(), "Alpha Role", RootBuId, "Root BU");
            var buZ = Guid.NewGuid();
            var roleAInOtherBu = fake.SeedRole(Guid.NewGuid(), "Alpha Role", buZ, "Zulu BU");

            fake.SeedUserRole(user.Id, roleB.Id);
            fake.SeedUserRole(user.Id, roleA.Id);
            fake.SeedUserRole(user.Id, roleAInOtherBu.Id);

            var sut = new TeamRoleInspectionService(fake);

            var result = sut.GetAssignments(user.Id);

            var ordered = result.Assignments.Select(a => (a.RoleName, a.RoleBusinessUnitName)).ToList();
            Assert.Equal(new[]
            {
                ("Alpha Role", "Root BU"),
                ("Alpha Role", "Zulu BU"),
                ("Bravo Role", "Root BU")
            }, ordered);
        }

        [Fact]
        public void GetAssignments_UserInAccessTeamWithNoRoles_ContributesNoAssignments()
        {
            // Access teams can't hold security roles (see the Assigner's docs); membership in one
            // with zero teamroles must not error and must not fabricate a row.
            var fake = new FakeOrganizationService();
            var user = fake.SeedUser(Guid.NewGuid(), "Erin", RootBuId, "Root BU");
            var accessTeam = fake.SeedTeam(Guid.NewGuid(), "Support Access Team", RootBuId, "Root BU");
            fake.SeedTeamMembership(user.Id, accessTeam.Id);
            var sut = new TeamRoleInspectionService(fake);

            var result = sut.GetAssignments(user.Id);

            Assert.Empty(result.Assignments);
        }

        [Fact]
        public void GetAssignments_UserWithZeroAssignments_ReturnsEmptyResultWithUserInfo()
        {
            var fake = new FakeOrganizationService();
            var user = fake.SeedUser(Guid.NewGuid(), "Frank", RootBuId, "Root BU", isDisabled: true);
            var sut = new TeamRoleInspectionService(fake);

            var result = sut.GetAssignments(user.Id);

            Assert.Empty(result.Assignments);
            Assert.Equal("Frank", result.UserName);
            Assert.True(result.IsDisabled);
            Assert.Equal(RootBuId, result.HomeBusinessUnitId);
        }

        [Fact]
        public void RetrieveUsers_PopulatesDirectAndTeamCounts_MatchingGetAssignmentsRowCounts()
        {
            var fake = new FakeOrganizationService();
            var user = fake.SeedUser(Guid.NewGuid(), "Grace", RootBuId, "Root BU");
            var directRole = fake.SeedRole(Guid.NewGuid(), "Marketing", RootBuId, "Root BU");
            fake.SeedUserRole(user.Id, directRole.Id);

            var role = fake.SeedRole(Guid.NewGuid(), "Salesperson", RootBuId, "Root BU");
            var teamA = fake.SeedTeam(Guid.NewGuid(), "Team A", RootBuId, "Root BU");
            var teamB = fake.SeedTeam(Guid.NewGuid(), "Team B", RootBuId, "Root BU");
            fake.SeedTeamMembership(user.Id, teamA.Id);
            fake.SeedTeamMembership(user.Id, teamB.Id);
            fake.SeedTeamRole(teamA.Id, role.Id);
            fake.SeedTeamRole(teamB.Id, role.Id);

            var otherUser = fake.SeedUser(Guid.NewGuid(), "Henry", RootBuId, "Root BU");

            var sut = new TeamRoleInspectionService(fake);

            var users = sut.RetrieveUsers();

            var grace = users.Single(u => u.Id == user.Id);
            Assert.Equal(1, grace.DirectCount);
            Assert.Equal(2, grace.TeamCount);

            var henry = users.Single(u => u.Id == otherUser.Id);
            Assert.Equal(0, henry.DirectCount);
            Assert.Equal(0, henry.TeamCount);
        }

        [Fact]
        public void GetTeamDetail_ReturnsTeamsOwnRolesAndMemberUsers()
        {
            var fake = new FakeOrganizationService();
            var team = fake.SeedTeam(Guid.NewGuid(), "Sales Team", RootBuId, "Root BU");
            var role = fake.SeedRole(Guid.NewGuid(), "Salesperson", RootBuId, "Root BU");
            fake.SeedTeamRole(team.Id, role.Id);

            var member = fake.SeedUser(Guid.NewGuid(), "Ivy", RootBuId, "Root BU");
            var disabledMember = fake.SeedUser(Guid.NewGuid(), "Jack", RootBuId, "Root BU", isDisabled: true);
            fake.SeedTeamMembership(member.Id, team.Id);
            fake.SeedTeamMembership(disabledMember.Id, team.Id);

            var sut = new TeamRoleInspectionService(fake);

            var result = sut.GetTeamDetail(team.Id);

            Assert.Equal("Sales Team", result.TeamName);
            Assert.Equal(RootBuId, result.BusinessUnitId);

            var teamRole = Assert.Single(result.Roles);
            Assert.Equal(role.Id, teamRole.RoleId);
            Assert.Equal("Salesperson", teamRole.RoleName);
            Assert.Equal(RootBuId, teamRole.RoleBusinessUnitId);

            Assert.Equal(2, result.Members.Count);
            Assert.Contains(result.Members, m => m.UserId == member.Id && !m.IsDisabled);
            Assert.Contains(result.Members, m => m.UserId == disabledMember.Id && m.IsDisabled);
        }

        [Fact]
        public void GetTeamDetail_MultipleRoles_AreOrderedByRoleThenRoleBu()
        {
            var fake = new FakeOrganizationService();
            var team = fake.SeedTeam(Guid.NewGuid(), "Sales Team", RootBuId, "Root BU");

            var roleB = fake.SeedRole(Guid.NewGuid(), "Bravo Role", RootBuId, "Root BU");
            var roleA = fake.SeedRole(Guid.NewGuid(), "Alpha Role", RootBuId, "Root BU");
            var buZ = Guid.NewGuid();
            var roleAInOtherBu = fake.SeedRole(Guid.NewGuid(), "Alpha Role", buZ, "Zulu BU");

            fake.SeedTeamRole(team.Id, roleB.Id);
            fake.SeedTeamRole(team.Id, roleA.Id);
            fake.SeedTeamRole(team.Id, roleAInOtherBu.Id);

            var sut = new TeamRoleInspectionService(fake);

            var result = sut.GetTeamDetail(team.Id);

            var ordered = result.Roles.Select(r => (r.RoleName, r.RoleBusinessUnitName)).ToList();
            Assert.Equal(new[]
            {
                ("Alpha Role", "Root BU"),
                ("Alpha Role", "Zulu BU"),
                ("Bravo Role", "Root BU")
            }, ordered);
        }

        [Fact]
        public void RetrieveTeams_PopulatesRoleAndMemberCounts()
        {
            var fake = new FakeOrganizationService();
            var team = fake.SeedTeam(Guid.NewGuid(), "Sales Team", RootBuId, "Root BU");
            var role = fake.SeedRole(Guid.NewGuid(), "Salesperson", RootBuId, "Root BU");
            fake.SeedTeamRole(team.Id, role.Id);
            var member = fake.SeedUser(Guid.NewGuid(), "Ivy", RootBuId, "Root BU");
            fake.SeedTeamMembership(member.Id, team.Id);

            var emptyTeam = fake.SeedTeam(Guid.NewGuid(), "Empty Team", RootBuId, "Root BU");

            var sut = new TeamRoleInspectionService(fake);

            var teams = sut.RetrieveTeams();

            var salesTeam = teams.Single(t => t.Id == team.Id);
            Assert.Equal(1, salesTeam.RoleCount);
            Assert.Equal(1, salesTeam.MemberCount);

            var empty = teams.Single(t => t.Id == emptyTeam.Id);
            Assert.Equal(0, empty.RoleCount);
            Assert.Equal(0, empty.MemberCount);
        }

        [Fact]
        public void RetrieveTeams_IgnoresPowerVirtualAgentTeamsByDefault_ButCanIncludeThem()
        {
            var fake = new FakeOrganizationService();
            var agentTeam = fake.SeedTeamWithDescription(
                Guid.NewGuid(), "Agent Team", RootBuId, "Root BU", "Power Virtual Agents default team");
            var normalTeam = fake.SeedTeamWithDescription(
                Guid.NewGuid(), "Sales Team", RootBuId, "Root BU", "Used by sales");
            var noDescriptionTeam = fake.SeedTeamWithDescription(
                Guid.NewGuid(), "Operations Team", RootBuId, "Root BU", null);

            var sut = new TeamRoleInspectionService(fake);

            var ignoredByDefault = sut.RetrieveTeams();
            var allTeams = sut.RetrieveTeams(ignoreAgentTeams: false);

            Assert.DoesNotContain(ignoredByDefault, t => t.Id == agentTeam.Id);
            Assert.Contains(ignoredByDefault, t => t.Id == normalTeam.Id);
            Assert.Contains(ignoredByDefault, t => t.Id == noDescriptionTeam.Id);
            Assert.Contains(allTeams, t => t.Id == agentTeam.Id);
        }
    }
}
