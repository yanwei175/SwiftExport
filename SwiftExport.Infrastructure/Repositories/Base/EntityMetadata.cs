namespace SwiftExport.Infrastructure.Repositories.Base
{
    public sealed class EntityMetadata
    {
        public required Type EntityType { get; init; }

        public required string TableName { get; init; }

        public required string KeyName { get; init; }

        public required string[] AllColumns { get; init; }

        public required string[] InsertColumns { get; init; }

        public required string[] UpdateColumns { get; init; }

        public bool HasVersion { get; init; }

        public bool HasSoftDelete { get; init; }
    }
}
