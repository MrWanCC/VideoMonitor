namespace VideoMonitor.Wpf.Catalog;

public sealed class CatalogMutationUncertainException : Exception
{
    public CatalogMutationUncertainException(
        string operation,
        Guid entityId,
        Exception? innerException = null)
        : base("The Catalog mutation result could not be confirmed.", innerException)
    {
        Operation = operation;
        EntityId = entityId;
    }

    public string Operation { get; }

    public Guid EntityId { get; }
}
