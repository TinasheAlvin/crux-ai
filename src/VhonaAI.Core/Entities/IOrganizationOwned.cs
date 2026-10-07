namespace VhonaAI.Core.Entities;

/// <summary>
/// A row that belongs to one business. The database applies a tenant filter to these types.
/// </summary>
public interface IOrganizationOwned
{
    Guid OrganizationId { get; }
}
