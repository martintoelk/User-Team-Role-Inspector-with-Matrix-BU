using System;
using System.Collections.Generic;

namespace UserTeamRoleInspector.Core
{
    /// <summary>
    /// The payload "BU Matrix Security Role Assigner" hands this tool over XrmToolBox's message
    /// bus: "here is the team or user I have selected - show me its roles".
    /// <para>
    /// <b>This is a wire format, not an object contract.</b> The sender ships as a separate
    /// plugin assembly from a separate repo and the two never reference each other, so an
    /// instance of the sender's own class would arrive on <c>MessageBusEventArgs.TargetArgument</c>
    /// as a type this assembly cannot name. What actually crosses the boundary is a string, and
    /// this is our own reader for it - the sender has its own writer. Keep the two in step:
    /// <c>RoleHandoffTests</c> on both sides pins the same literal payloads for exactly that
    /// reason.
    /// </para>
    /// <para>
    /// Format: <c>xtbrolehandoff:v=1&amp;entity=team&amp;id=&lt;guid&gt;&amp;name=&lt;name&gt;</c>,
    /// optionally <c>&amp;buid=&lt;guid&gt;&amp;bu=&lt;name&gt;</c>. Values are escaped with
    /// <see cref="Uri.EscapeDataString"/>, since Dataverse names may contain any of the
    /// separators. Keys may be added while <c>v</c> stays 1 - unknown keys are ignored - so
    /// <c>v</c> only moves when the meaning of an existing key changes, and we refuse a version
    /// this build was not written for rather than guess at it.
    /// </para>
    /// </summary>
    public class RoleHandoff
    {
        private const string Prefix = "xtbrolehandoff:";
        private const string Version = "1";

        /// <summary>Logical name of the record to open: <c>team</c> or <c>systemuser</c>.</summary>
        public string Entity { get; set; }

        /// <summary>Id of the team/user to open. Everything shown is re-resolved from it.</summary>
        public Guid Id { get; set; }

        /// <summary>Display name, for saying what we failed to open when <see cref="Id"/> resolves to nothing.</summary>
        public string Name { get; set; }

        /// <summary>Owning business unit, when the sender knew it. Context only.</summary>
        public Guid? BusinessUnitId { get; set; }

        /// <summary>Owning business unit's name, when the sender knew it.</summary>
        public string BusinessUnitName { get; set; }

        /// <summary>
        /// Reads a handoff off a <c>MessageBusEventArgs.TargetArgument</c>. That argument is
        /// <c>dynamic</c> and any tool may be the sender, so this takes <see cref="object"/> and
        /// answers false - rather than throwing - for anything that is not one of ours. A tool
        /// that cannot act on a message should ignore it, not fail in front of the user.
        /// </summary>
        public static bool TryParse(object payload, out RoleHandoff handoff)
        {
            handoff = null;

            if (!(payload is string text)) return false;
            if (!text.StartsWith(Prefix, StringComparison.Ordinal)) return false;

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in text.Substring(Prefix.Length).Split('&'))
            {
                if (pair.Length == 0) continue;
                var split = pair.IndexOf('=');
                if (split <= 0) continue;
                // Last one wins; a duplicated key is malformed either way, and this keeps the
                // parse total rather than adding a failure mode nobody can act on.
                values[pair.Substring(0, split)] = pair.Substring(split + 1);
            }

            if (!values.TryGetValue("v", out var version) || version != Version) return false;
            if (!values.TryGetValue("entity", out var entity) || entity.Length == 0) return false;
            if (!values.TryGetValue("id", out var rawId)) return false;
            if (!Guid.TryParse(rawId, out var id) || id == Guid.Empty) return false;

            handoff = new RoleHandoff
            {
                Entity = Unescape(entity),
                Id = id,
                Name = values.TryGetValue("name", out var name) ? Unescape(name) : null,
                BusinessUnitId = values.TryGetValue("buid", out var rawBuId) && Guid.TryParse(rawBuId, out var buId) && buId != Guid.Empty
                    ? buId
                    : (Guid?)null,
                BusinessUnitName = values.TryGetValue("bu", out var bu) ? Unescape(bu) : null
            };
            return true;
        }

        private static string Unescape(string value)
        {
            // A stray '%' that isn't a valid escape can make UnescapeDataString throw on .NET
            // Framework. The payload is off the wire, so take what we were given rather than
            // letting it surface as an exception in the UI.
            try { return Uri.UnescapeDataString(value); }
            catch (UriFormatException) { return value; }
        }
    }
}
