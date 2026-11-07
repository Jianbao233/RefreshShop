using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;

namespace RefreshShop;

// The mod stays independent of a particular sts2.dll build. Missing required members
// throw before a refresh is committed; there are no random or payment fallbacks.
internal static class GameApi
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static readonly ConcurrentDictionary<(Type, string), PropertyInfo> Properties = new();
    private static readonly ConcurrentDictionary<(Type, string), FieldInfo> Fields = new();

    public static Type Find(string name) => AppDomain.CurrentDomain.GetAssemblies()
        .Select(a => a.GetType(name)).FirstOrDefault(t => t != null);
    public static Type Type(string name) => Find("MegaCrit.Sts2.Core." + name)
        ?? throw new TypeLoadException(name);
    public static PropertyInfo Property(Type type, string name) => Properties.GetOrAdd((type, name), key =>
        key.Item1.GetProperty(key.Item2, Flags) ?? throw new MissingMemberException(key.Item1.FullName, key.Item2));
    public static FieldInfo Field(Type type, string name) => Fields.GetOrAdd((type, name), key =>
    {
        for (var t = key.Item1; t != null; t = t.BaseType)
        {
            var field = t.GetField(key.Item2, Flags | BindingFlags.DeclaredOnly);
            if (field != null) return field;
        }
        throw new MissingFieldException(key.Item1.FullName, key.Item2);
    });
    public static object Get(object target, string name) => Property(target as Type ?? target.GetType(), name)
        .GetValue(target is Type ? null : target);
    public static void Set(object target, string name, object value) => Property(target.GetType(), name).SetValue(target, value);
    public static object Read(object target, string name) => Field(target.GetType(), name).GetValue(target);
    public static void Write(object target, string name, object value) => Field(target.GetType(), name).SetValue(target, value);
    public static MethodInfo FindMethod(Type type, string name, params Type[] args) => type.GetMethod(name, Flags, null, args, null);
    public static MethodInfo Method(Type type, string name, params Type[] args) => FindMethod(type, name, args)
        ?? throw new MissingMethodException(type.FullName, name);
    public static object Call(object target, string name, params object[] args)
    {
        var type = target as Type ?? target.GetType();
        // Select by assignable parameter types, never by parameter count alone.
        var methods = type.GetMethods(Flags).Where(m => m.Name == name && !m.ContainsGenericParameters
            && m.GetParameters().Length == args.Length
            && m.GetParameters().Select((p, i) => args[i] == null || p.ParameterType.IsInstanceOfType(args[i])).All(b => b)).ToArray();
        if (methods.Length != 1) throw new MissingMethodException(type.FullName, name + " (unique signature)");
        return methods[0].Invoke(target is Type ? null : target, args);
    }
    public static object[] Items(object list) => ((IEnumerable)list).Cast<object>().ToArray();
    public static string Id(object model) => (string)Get(Get(model, "Id"), "Entry");
    public static object Clone(object value) => Method(typeof(object), "MemberwiseClone").Invoke(value, null);
    public static object RunManager => Get(Type("Runs.RunManager"), "Instance");
    public static bool IsLocal(object player) => (bool)Call(Type("Context.LocalContext"), "IsMe", player);
    public static object Inventory(object node) => Get(node, "Inventory");
    public static object Player(object inventory) => Get(inventory, "Player");
    public static int Gold(object player) => (int)Get(player, "Gold");
    public static object PotionOptions(object player)
    {
        var factory = Type("Factories.PotionFactory");
        var current = factory.GetMethod("GetPotionOptions", Flags, null, new[] { player.GetType() }, null);
        if (current != null) return current.Invoke(null, new[] { player });
        // v0.107.1 has an explicit optional-blacklist argument in this API.
        var listType = typeof(System.Collections.Generic.IEnumerable<>).MakeGenericType(Type("Models.PotionModel"));
        return Method(factory, "GetPotionOptions", player.GetType(), listType).Invoke(null,
            new[] { player, Array.CreateInstance(Type("Models.PotionModel"), 0) });
    }
}
