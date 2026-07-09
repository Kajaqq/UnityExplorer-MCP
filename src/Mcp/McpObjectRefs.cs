using System;
using System.Collections.Generic;
using System.Threading;

#nullable enable

namespace UnityExplorer.Mcp
{
    internal sealed record McpObjectRefMetadata(
        string Kind,
        string RuntimeType,
        string? Name,
        string? Path,
        DateTime CreatedUtc,
        IReadOnlyList<string>? Tags);

    internal static class McpObjectRefs
    {
        private const int MaxRefs = 8192;
        private static readonly object Gate = new();
        private static readonly Dictionary<string, object> ById = new Dictionary<string, object>(StringComparer.Ordinal);
        private static readonly Dictionary<string, McpObjectRefMetadata> MetadataById = new Dictionary<string, McpObjectRefMetadata>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> StableIdByKey = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> StableKeyById = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Queue<string> Order = new Queue<string>();
        private static long _nextId;
        private static readonly string Prefix = "ref:" + Guid.NewGuid().ToString("N").Substring(0, 8) + ":";

        private static bool IsNullOrWhiteSpace(string? value)
        {
            if (value == null) return true;
            for (var i = 0; i < value.Length; i++)
            {
                if (!char.IsWhiteSpace(value[i]))
                    return false;
            }
            return true;
        }

        public static string Capture(object value)
            => Capture(value, null);

        public static string Capture(object value, McpObjectRefMetadata? metadata)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            var id = Prefix + Interlocked.Increment(ref _nextId);
            lock (Gate)
            {
                ById[id] = value;
                if (metadata != null)
                    MetadataById[id] = metadata;
                Order.Enqueue(id);
                TrimToCapacity();
            }
            return id;
        }

        public static string CaptureStable(string stableKey, object value, McpObjectRefMetadata? metadata)
        {
            if (IsNullOrWhiteSpace(stableKey)) throw new ArgumentException("stableKey is required", nameof(stableKey));
            if (value == null) throw new ArgumentNullException(nameof(value));

            lock (Gate)
            {
                if (StableIdByKey.TryGetValue(stableKey, out var existingId) && ById.ContainsKey(existingId))
                {
                    ById[existingId] = value;
                    if (metadata != null)
                        MetadataById[existingId] = metadata;
                    return existingId;
                }

                if (!string.IsNullOrEmpty(existingId))
                {
                    StableIdByKey.Remove(stableKey);
                    StableKeyById.Remove(existingId);
                }

                var id = Prefix + Interlocked.Increment(ref _nextId);
                ById[id] = value;
                if (metadata != null)
                    MetadataById[id] = metadata;
                StableIdByKey[stableKey] = id;
                StableKeyById[id] = stableKey;
                Order.Enqueue(id);
                TrimToCapacity();
                return id;
            }
        }

        public static bool TryGet(string refId, out object? value)
        {
            if (IsNullOrWhiteSpace(refId))
            {
                value = null;
                return false;
            }

            lock (Gate)
            {
                return ById.TryGetValue(refId, out value);
            }
        }

        public static bool TryGetMetadata(string refId, out McpObjectRefMetadata? metadata)
        {
            if (IsNullOrWhiteSpace(refId))
            {
                metadata = null;
                return false;
            }

            lock (Gate)
            {
                return MetadataById.TryGetValue(refId, out metadata);
            }
        }

        public static bool Release(string refId)
        {
            if (IsNullOrWhiteSpace(refId)) return false;
            lock (Gate)
            {
                MetadataById.Remove(refId);
                if (StableKeyById.TryGetValue(refId, out var stableKey))
                {
                    StableKeyById.Remove(refId);
                    StableIdByKey.Remove(stableKey);
                }
                return ById.Remove(refId);
            }
        }

        private static void TrimToCapacity()
        {
            while (Order.Count > MaxRefs)
            {
                var old = Order.Dequeue();
                ById.Remove(old);
                MetadataById.Remove(old);
                if (StableKeyById.TryGetValue(old, out var stableKey))
                {
                    StableKeyById.Remove(old);
                    StableIdByKey.Remove(stableKey);
                }
            }
        }
    }
}
