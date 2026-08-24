using System;
using Xunit;

namespace UserTeamRoleInspector.Core.Tests
{
    /// <summary>
    /// Tests for <see cref="RoleHandoff"/> - the payload "BU Matrix Security Role Assigner" hands
    /// this tool over XrmToolBox's message bus (issue #17 in that repo).
    /// <para>
    /// Every payload here is a literal string, never one built by our own encoder: the sender is
    /// a separately built, separately versioned assembly, so what has to keep working is the wire
    /// format itself. A round-trip test would stay green through a format change that breaks
    /// every Role Assigner build already installed.
    /// </para>
    /// </summary>
    public class RoleHandoffTests
    {
        private const string UserPayload =
            "xtbrolehandoff:v=1&entity=systemuser&id=11111111-1111-1111-1111-111111111111" +
            "&name=Ada%20Lovelace&buid=22222222-2222-2222-2222-222222222222&bu=Contoso%20Ltd";

        [Fact]
        public void ParsesAUserHandoff()
        {
            Assert.True(RoleHandoff.TryParse(UserPayload, out var handoff));
            Assert.Equal("systemuser", handoff.Entity);
            Assert.Equal(new Guid("11111111-1111-1111-1111-111111111111"), handoff.Id);
            Assert.Equal("Ada Lovelace", handoff.Name);
            Assert.Equal(new Guid("22222222-2222-2222-2222-222222222222"), handoff.BusinessUnitId);
            Assert.Equal("Contoso Ltd", handoff.BusinessUnitName);
        }

        [Fact]
        public void ParsesATeamHandoffWithNoBusinessUnitContext()
        {
            Assert.True(RoleHandoff.TryParse(
                "xtbrolehandoff:v=1&entity=team&id=33333333-3333-3333-3333-333333333333&name=Sales%20Managers",
                out var handoff));

            Assert.Equal("team", handoff.Entity);
            Assert.Equal("Sales Managers", handoff.Name);
            Assert.Null(handoff.BusinessUnitId);
            Assert.Null(handoff.BusinessUnitName);
        }

        // Dataverse names can carry the very characters the payload uses as separators.
        [Theory]
        [InlineData("A%20%26%20B%20%3D%20C", "A & B = C")]
        [InlineData("name%3Fwith%26every%3Dseparator", "name?with&every=separator")]
        [InlineData("%C3%9Cn%C3%AFc%C3%B6de%20%E5%90%8D%E5%89%8D", "Ünïcöde 名前")]
        [InlineData("", "")]
        public void UnescapesNames(string escaped, string expected)
        {
            Assert.True(RoleHandoff.TryParse(
                $"xtbrolehandoff:v=1&entity=team&id=33333333-3333-3333-3333-333333333333&name={escaped}",
                out var handoff));

            Assert.Equal(expected, handoff.Name);
        }

        // The sender may add keys while v stays 1, so an older build of this tool has to ignore
        // what it doesn't know rather than reject the whole handoff.
        [Fact]
        public void IgnoresKeysItDoesNotKnow()
        {
            Assert.True(RoleHandoff.TryParse(
                "xtbrolehandoff:v=1&entity=team&id=33333333-3333-3333-3333-333333333333&somethingnew=x",
                out _));
        }

        [Theory]
        [InlineData(null)]                                          // no payload at all
        [InlineData("")]                                            // empty payload
        [InlineData("<fetch><entity name='account' /></fetch>")]    // another tool's payload
        [InlineData("xtbrolehandoff:v=1&entity=team")]              // no id
        [InlineData("xtbrolehandoff:v=1&entity=team&id=not-a-guid")]// unparsable id
        [InlineData("xtbrolehandoff:v=1&id=33333333-3333-3333-3333-333333333333")] // no entity
        [InlineData("xtbrolehandoff:v=2&entity=team&id=33333333-3333-3333-3333-333333333333")] // future format
        [InlineData("xtbrolehandoff:entity=team&id=33333333-3333-3333-3333-333333333333")] // unversioned
        public void RejectsAnythingItCannotSafelyAct(string payload)
        {
            Assert.False(RoleHandoff.TryParse(payload, out var handoff));
            Assert.Null(handoff);
        }

        // TargetArgument is dynamic, so any tool can hand us any object.
        [Fact]
        public void RejectsANonStringPayload()
        {
            Assert.False(RoleHandoff.TryParse(new object(), out var handoff));
            Assert.Null(handoff);
        }

        [Fact]
        public void RejectsAnAllZeroId()
        {
            Assert.False(RoleHandoff.TryParse(
                "xtbrolehandoff:v=1&entity=team&id=00000000-0000-0000-0000-000000000000", out _));
        }

        // A malformed escape would otherwise surface as a UriFormatException in the UI.
        [Fact]
        public void SurvivesAMalformedEscapeInAName()
        {
            Assert.True(RoleHandoff.TryParse(
                "xtbrolehandoff:v=1&entity=team&id=33333333-3333-3333-3333-333333333333&name=100%",
                out var handoff));

            Assert.Equal("100%", handoff.Name);
        }
    }
}
