using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace UndoModMS.Snapshot;

internal static class DeepCloner
{
    public static T? Clone<T>(T? obj) where T : class
        => (T?)CloneInternal(obj, new Dictionary<object, object>(ReferenceEqualityComparer.Instance));

    public static object? CloneObject(object? obj)
        => CloneInternal(obj, new Dictionary<object, object>(ReferenceEqualityComparer.Instance));

    private static object? CloneInternal(object? obj, Dictionary<object, object> seen)
    {
        if (obj == null) return null;
        var type = obj.GetType();

        if (IsImmutable(type)) return obj;
        if (obj is Delegate) return obj;
        if (obj is Type or MemberInfo) return obj;
        if (obj is Godot.GodotObject) return obj;

        if (seen.TryGetValue(obj, out var existing)) return existing;

        if (type.IsArray)
        {
            var src = (Array)obj;
            var elem = type.GetElementType()!;
            var dst = Array.CreateInstance(elem, src.Length);
            seen[obj] = dst;
            for (int i = 0; i < src.Length; i++)
                dst.SetValue(CloneInternal(src.GetValue(i), seen), i);
            return dst;
        }

        object clone;
        try
        {
            clone = RuntimeHelpers.GetUninitializedObject(type);
        }
        catch
        {
            return obj;
        }

        seen[obj] = clone;

        foreach (var field in GetCloneFields(type))
        {
            object? value;
            try { value = field.GetValue(obj); }
            catch { continue; }
            try { field.SetValue(clone, CloneInternal(value, seen)); }
            catch { }
        }

        return clone;
    }

    private static readonly ConcurrentDictionary<Type, FieldInfo[]> _fieldCache = new();

    private static FieldInfo[] GetCloneFields(Type type)
        => _fieldCache.GetOrAdd(type, BuildCloneFields);

    private static FieldInfo[] BuildCloneFields(Type type)
    {
        var list = new List<FieldInfo>();
        for (var t = type; t != null && t != typeof(object); t = t.BaseType)
        {
            list.AddRange(t.GetFields(BindingFlags.Public | BindingFlags.NonPublic
                                      | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        }
        return list.ToArray();
    }

    private static bool IsImmutable(Type t)
    {
        if (t.IsPrimitive) return true;
        if (t.IsEnum) return true;
        if (t == typeof(string)) return true;
        if (t == typeof(decimal)) return true;
        if (t == typeof(DateTime)) return true;
        if (t == typeof(Guid)) return true;
        return false;
    }
}