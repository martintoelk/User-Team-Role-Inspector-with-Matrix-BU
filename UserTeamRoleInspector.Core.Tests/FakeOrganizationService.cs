using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace UserTeamRoleInspector.Core.Tests
{
    /// <summary>
    /// Hand-rolled in-memory IOrganizationService double, mirroring the Assigner's fake.
    /// Supports just enough of the SDK surface that TeamRoleInspectionService exercises:
    /// Retrieve by id, and RetrieveMultiple over a single base entity with an optional
    /// single-level LinkEntity join (Equal-only base/link criteria, paging via
    /// PageInfo.Count/PageNumber), including projecting a link's EntityAlias/Columns onto the
    /// base row as AliasedValue - needed for RetrieveMemberTeams' team.* columns.
    /// </summary>
    public sealed class FakeOrganizationService : IOrganizationService
    {
        private readonly Dictionary<string, Dictionary<Guid, Entity>> _tables =
            new Dictionary<string, Dictionary<Guid, Entity>>(StringComparer.OrdinalIgnoreCase);

        public Entity Seed(Entity entity)
        {
            if (entity.Id == Guid.Empty)
                entity.Id = Guid.NewGuid();
            GetOrCreateTable(entity.LogicalName)[entity.Id] = entity;
            return entity;
        }

        public Entity SeedUser(Guid id, string fullName, Guid businessUnitId, string businessUnitName, bool isDisabled = false)
        {
            var user = new Entity("systemuser", id) { ["fullname"] = fullName, ["isdisabled"] = isDisabled };
            if (businessUnitId != Guid.Empty)
                user["businessunitid"] = new EntityReference("businessunit", businessUnitId) { Name = businessUnitName };
            return Seed(user);
        }

        public Entity SeedRole(Guid id, string name, Guid businessUnitId, string businessUnitName)
        {
            var role = new Entity("role", id) { ["name"] = name };
            if (businessUnitId != Guid.Empty)
                role["businessunitid"] = new EntityReference("businessunit", businessUnitId) { Name = businessUnitName };
            return Seed(role);
        }

        public Entity SeedTeam(Guid id, string name, Guid businessUnitId, string businessUnitName)
        {
            var team = new Entity("team", id) { ["name"] = name };
            if (businessUnitId != Guid.Empty)
                team["businessunitid"] = new EntityReference("businessunit", businessUnitId) { Name = businessUnitName };
            return Seed(team);
        }

        public Entity SeedTeamWithDescription(Guid id, string name, Guid businessUnitId, string businessUnitName, string description)
        {
            var team = SeedTeam(id, name, businessUnitId, businessUnitName);
            if (description != null)
                team["description"] = description;
            return team;
        }

        /// <summary>Directly seeds a teammembership intersect row (user belongs to team).</summary>
        public void SeedTeamMembership(Guid userId, Guid teamId)
        {
            var row = new Entity("teammembership", Guid.NewGuid());
            row["systemuserid"] = userId;
            row["teamid"] = teamId;
            Seed(row);
        }

        /// <summary>Directly seeds a teamroles intersect row.</summary>
        public void SeedTeamRole(Guid teamId, Guid roleId)
        {
            var row = new Entity("teamroles", Guid.NewGuid());
            row["teamid"] = teamId;
            row["roleid"] = roleId;
            Seed(row);
        }

        /// <summary>Directly seeds a systemuserroles intersect row.</summary>
        public void SeedUserRole(Guid userId, Guid roleId)
        {
            var row = new Entity("systemuserroles", Guid.NewGuid());
            row["systemuserid"] = userId;
            row["roleid"] = roleId;
            Seed(row);
        }

        public Guid Create(Entity entity)
        {
            if (entity.Id == Guid.Empty)
                entity.Id = Guid.NewGuid();
            GetOrCreateTable(entity.LogicalName)[entity.Id] = entity;
            return entity.Id;
        }

        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            if (_tables.TryGetValue(entityName, out var table) && table.TryGetValue(id, out var entity))
                return entity;
            throw new InvalidOperationException($"{entityName} {id} does not exist.");
        }

        public void Update(Entity entity) => throw new NotSupportedException();

        public void Delete(string entityName, Guid id) => throw new NotSupportedException();

        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
            => throw new NotSupportedException();

        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
            => throw new NotSupportedException();

        public OrganizationResponse Execute(OrganizationRequest request)
        {
            throw new NotSupportedException($"FakeOrganizationService does not implement Execute for '{request.RequestName}'.");
        }

        public EntityCollection RetrieveMultiple(QueryBase queryBase)
        {
            if (!(queryBase is QueryExpression query))
                throw new NotSupportedException("FakeOrganizationService only supports QueryExpression.");

            IEnumerable<Entity> rows = _tables.TryGetValue(query.EntityName, out var baseTable)
                ? baseTable.Values
                : Enumerable.Empty<Entity>();

            if (query.Criteria != null)
                rows = rows.Where(e => MatchesFilter(e, query.Criteria));

            foreach (var link in query.LinkEntities)
                rows = ApplyLink(rows, link);

            var all = rows.ToList();

            var pageNumber = query.PageInfo != null && query.PageInfo.PageNumber > 0 ? query.PageInfo.PageNumber : 1;
            var pageSize = query.PageInfo != null && query.PageInfo.Count > 0 ? query.PageInfo.Count : all.Count;
            var page = pageSize > 0
                ? all.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList()
                : all;

            var moreRecords = pageSize > 0 && pageNumber * pageSize < all.Count;
            return new EntityCollection(page)
            {
                MoreRecords = moreRecords,
                PagingCookie = moreRecords ? $"page={pageNumber + 1}" : null,
                TotalRecordCount = all.Count
            };
        }

        /// <summary>
        /// Inner-joins base rows against the link's target table, matching
        /// LinkFromAttributeName (on the base row) to LinkToAttributeName (on the linked row) and
        /// the link's own LinkCriteria; when the link carries an EntityAlias with Columns, the
        /// matched linked row's requested columns are copied onto the base row as
        /// "alias.attribute" AliasedValue entries (what QueryExpression does for real against
        /// Dataverse), which RetrieveMemberTeams reads back via GetAliased.
        /// </summary>
        private IEnumerable<Entity> ApplyLink(IEnumerable<Entity> rows, LinkEntity link)
        {
            var linkedRows = _tables.TryGetValue(link.LinkToEntityName, out var linkedTable)
                ? linkedTable.Values.AsEnumerable()
                : Enumerable.Empty<Entity>();

            if (link.LinkCriteria != null)
            {
                foreach (var condition in link.LinkCriteria.Conditions)
                    linkedRows = linkedRows.Where(e => MatchesCondition(e, condition));
            }
            var linkedRowList = linkedRows.ToList();

            var wantedColumns = link.Columns != null && !link.Columns.AllColumns
                ? link.Columns.Columns
                : null;

            var result = new List<Entity>();
            foreach (var baseRow in rows)
            {
                var fromValue = GetGuidValue(baseRow, link.LinkFromAttributeName);
                var match = linkedRowList.FirstOrDefault(linkedRow => GetGuidValue(linkedRow, link.LinkToAttributeName) == fromValue);
                if (match == null)
                    continue;

                if (!string.IsNullOrEmpty(link.EntityAlias))
                {
                    var columns = wantedColumns ?? match.Attributes.Keys;
                    foreach (var column in columns)
                    {
                        var value = string.Equals(column, link.LinkToEntityName + "id", StringComparison.OrdinalIgnoreCase)
                            ? (object)match.Id
                            : match.GetAttributeValue<object>(column);
                        baseRow[$"{link.EntityAlias}.{column}"] = new AliasedValue(link.LinkToEntityName, column, value);
                    }
                }

                result.Add(baseRow);
            }
            return result;
        }

        private Dictionary<Guid, Entity> GetOrCreateTable(string logicalName)
        {
            if (!_tables.TryGetValue(logicalName, out var table))
            {
                table = new Dictionary<Guid, Entity>();
                _tables[logicalName] = table;
            }
            return table;
        }

        /// <summary>
        /// Resolves an attribute to a Guid the way the join/filter logic needs: the entity's own
        /// primary-key attribute (e.g. "roleid" on a "role" row) falls back to Entity.Id, since
        /// seeded test entities don't duplicate that value into their attribute bag the way a real
        /// Dataverse row does; anything else reads the attribute (Guid or EntityReference.Id).
        /// </summary>
        private static Guid GetGuidValue(Entity entity, string attributeName)
        {
            if (string.Equals(attributeName, entity.LogicalName + "id", StringComparison.OrdinalIgnoreCase))
                return entity.Id;

            var value = entity.GetAttributeValue<object>(attributeName);
            switch (value)
            {
                case Guid guid:
                    return guid;
                case EntityReference reference:
                    return reference.Id;
                default:
                    return Guid.Empty;
            }
        }

        private static bool MatchesCondition(Entity entity, ConditionExpression condition)
        {
            var actual = entity.GetAttributeValue<object>(condition.AttributeName);

            if (condition.Operator == ConditionOperator.Null)
                return actual == null;

            if (condition.Operator == ConditionOperator.NotNull)
                return actual != null;

            if (condition.Values.Count != 1)
                return true; // unsupported operators are permissive - out of scope for this fake

            if (condition.Values[0] is Guid expectedGuid)
                return GetGuidValue(entity, condition.AttributeName) == expectedGuid;

            if (condition.Operator == ConditionOperator.NotLike)
            {
                if (!(actual is string actualText)) return false;
                var pattern = Convert.ToString(condition.Values[0]) ?? string.Empty;
                var expectedText = pattern.Trim('%');
                return actualText.IndexOf(expectedText, StringComparison.OrdinalIgnoreCase) < 0;
            }

            if (condition.Operator != ConditionOperator.Equal)
                return true;

            return Equals(actual, condition.Values[0]);
        }

        private static bool MatchesFilter(Entity entity, FilterExpression filter)
        {
            var matchesConditions = filter.Conditions.All(condition => MatchesCondition(entity, condition));
            var matchesFilters = filter.Filters.All(nested => MatchesFilter(entity, nested));

            if (filter.FilterOperator == LogicalOperator.Or && (filter.Conditions.Count > 0 || filter.Filters.Count > 0))
            {
                return filter.Conditions.Any(condition => MatchesCondition(entity, condition)) ||
                       filter.Filters.Any(nested => MatchesFilter(entity, nested));
            }

            return matchesConditions && matchesFilters;
        }
    }
}
