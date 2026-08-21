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
    }
}
