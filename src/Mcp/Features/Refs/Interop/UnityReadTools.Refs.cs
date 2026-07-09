#if INTEROP
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UniverseLib.Utility;

namespace UnityExplorer.Mcp
{
    public static partial class UnityReadTools
    {
        [McpServerTool, Description("Inspect an object reference handle (ref:...).")]
        public static async Task<object> InspectRef(string refId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(refId) || !refId.StartsWith("ref:", StringComparison.Ordinal))
                throw new ArgumentException("Invalid refId; expected 'ref:...'", nameof(refId));

            return await MainThread.Run(() =>
            {
                if (!McpObjectRefs.TryGet(refId, out var instance) || instance == null)
                    throw new InvalidOperationException("NotFound");

                var t = instance.GetActualType() ?? instance.GetType();
                var typeName = SafeTypeName(t);
                var kind = instance is IDictionary
                    ? "dictionary"
                    : instance is IEnumerable && instance is not string ? "enumerable" : "object";

                int? count = null;
                try
                {
                    if (instance is ICollection coll) count = coll.Count;
                }
                catch { }

                string valueText;
                try { valueText = Truncate(instance.ToString() ?? "<null>"); }
                catch { valueText = typeName; }

                McpObjectRefs.TryGetMetadata(refId, out var metadata);
                return new { ok = true, refId, type = typeName, kind, count, valueText, metadata };
            });
        }

        [McpServerTool, Description("List members for an object reference handle (fields/properties, methods optional).")]
        public static async Task<Page<InspectorMemberDto>> ListRefMembers(string refId, bool includeMethods = false, int? limit = null, int? offset = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(refId) || !refId.StartsWith("ref:", StringComparison.Ordinal))
                throw new ArgumentException("Invalid refId; expected 'ref:...'", nameof(refId));

            int lim = Math.Max(1, limit ?? 100);
            int off = Math.Max(0, offset ?? 0);

            return await MainThread.Run(() =>
            {
                if (!McpObjectRefs.TryGet(refId, out var instance) || instance == null)
                    throw new InvalidOperationException("NotFound");

                var t = instance.GetActualType() ?? instance.GetType();
                var members = new List<InspectorMemberDto>();

                foreach (var fi in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (fi.IsSpecialName) continue;
                    var canWrite = !(fi.IsInitOnly || fi.IsLiteral);
                    members.Add(new InspectorMemberDto(fi.Name, "field", SafeTypeName(fi.FieldType), true, canWrite));
                }

                foreach (var pi in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (pi.IsSpecialName) continue;
                    if (pi.GetIndexParameters().Length > 0) continue;
                    var canRead = pi.GetGetMethod(true) != null;
                    var canWrite = pi.GetSetMethod(true) != null;
                    members.Add(new InspectorMemberDto(pi.Name, "property", SafeTypeName(pi.PropertyType), canRead, canWrite));
                }

                if (includeMethods)
                {
                    foreach (var mi in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (mi.IsSpecialName) continue;
                        members.Add(new InspectorMemberDto(mi.Name, "method", FormatMethodType(mi), false, false));
                    }
                }

                var total = members.Count;
                var items = members.Skip(off).Take(lim).ToArray();
                return new Page<InspectorMemberDto>(total, items);
            });
        }

        [McpServerTool, Description("Read a field or property value on an object reference handle (safe, bounded).")]
        public static async Task<object> ReadRefMember(string refId, string name, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(refId) || !refId.StartsWith("ref:", StringComparison.Ordinal))
                throw new ArgumentException("Invalid refId; expected 'ref:...'", nameof(refId));
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Invalid member name", nameof(name));

            return await MainThread.Run(() =>
            {
                if (!McpObjectRefs.TryGet(refId, out var instance) || instance == null)
                    throw new InvalidOperationException("NotFound");

                var t = instance.GetActualType() ?? instance.GetType();
                var fi = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fi != null)
                {
                    var val = fi.GetValue(instance);
                    var summary = SummarizeValue(val);
                    return new { ok = true, type = SafeTypeName(fi.FieldType), valueText = summary.Text, valueJson = summary.Json, refId = summary.RefId };
                }

                var pi = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (pi != null && pi.GetIndexParameters().Length == 0)
                {
                    var getter = pi.GetGetMethod(true);
                    if (getter == null)
                        throw new InvalidOperationException("NotFound");
                    var val = getter.Invoke(instance, Array.Empty<object>());
                    var summary = SummarizeValue(val);
                    return new { ok = true, type = SafeTypeName(pi.PropertyType), valueText = summary.Text, valueJson = summary.Json, refId = summary.RefId };
                }

                throw new InvalidOperationException("NotFound");
            });
        }

        [McpServerTool, Description("List items for an enumerable or dictionary reference handle (paged).")]
        public static async Task<Page<object>> ListRefItems(string refId, int? limit = null, int? offset = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(refId) || !refId.StartsWith("ref:", StringComparison.Ordinal))
                throw new ArgumentException("Invalid refId; expected 'ref:...'", nameof(refId));

            int lim = Math.Max(1, limit ?? 100);
            int off = Math.Max(0, offset ?? 0);

            return await MainThread.Run(() =>
            {
                if (!McpObjectRefs.TryGet(refId, out var instance) || instance == null)
                    throw new InvalidOperationException("NotFound");

                if (instance is IDictionary dict)
                {
                    var total = dict.Count;
                    var items = new List<object>(Math.Min(lim, total));
                    var idx = 0;
                    foreach (DictionaryEntry entry in dict)
                    {
                        if (idx++ < off) continue;
                        if (items.Count >= lim) break;
                        var k = SummarizeValue(entry.Key, depth: 1);
                        var v = SummarizeValue(entry.Value, depth: 1);
                        items.Add(new
                        {
                            key = new { text = k.Text, json = k.Json, refId = k.RefId },
                            value = new { text = v.Text, json = v.Json, refId = v.RefId }
                        });
                    }
                    return new Page<object>(total, items);
                }

                if (instance is IEnumerable enumerable && instance is not string)
                {
                    if (instance is not ICollection coll)
                        throw new ArgumentException("Enumerable does not provide Count; use preview from Read* tools.");

                    var total = coll.Count;
                    var items = new List<object>(Math.Min(lim, total));
                    var idx = 0;
                    foreach (var item in enumerable)
                    {
                        if (idx++ < off) continue;
                        if (items.Count >= lim) break;
                        var s = SummarizeValue(item, depth: 1);
                        items.Add(new { text = s.Text, json = s.Json, refId = s.RefId });
                    }
                    return new Page<object>(total, items);
                }

                throw new ArgumentException("NotSupported: ref is not enumerable/dictionary.");
            });
        }

        [McpServerTool, Description("Release an object reference handle from the server cache.")]
        public static object ReleaseRef(string refId)
        {
            if (string.IsNullOrWhiteSpace(refId) || !refId.StartsWith("ref:", StringComparison.Ordinal))
                throw new ArgumentException("Invalid refId; expected 'ref:...'", nameof(refId));

            var released = McpObjectRefs.Release(refId);
            return new { ok = true, released };
        }
    }
}
#endif

