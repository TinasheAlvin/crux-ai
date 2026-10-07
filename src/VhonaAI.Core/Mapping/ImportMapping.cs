using VhonaAI.Core.Entities;

namespace VhonaAI.Core.Mapping;

/// <summary>
/// Picks the mapping check for an import. Invoice files do not have Date or Description columns.
/// </summary>
public static class ImportMapping
{
    public static IReadOnlyList<string> Validate(ImportKind kind, IReadOnlyDictionary<string, string> mapping) =>
        kind == ImportKind.Invoices
            ? InvoiceMappingValidator.Validate(mapping)
            : MappingValidator.Validate(mapping);
}
