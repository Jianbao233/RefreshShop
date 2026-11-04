using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace RefreshShop;

// Prepare on shadow entries, retaining the real inventory, event handlers and UI nodes.
// Native factories run synchronously inside a private RNG scope. Neither success nor
// failure advances vanilla player RNGs, which is essential when peers do not have this mod.
internal sealed class RefreshPlan
{
    internal sealed record Change(object Entry, string Property, object Before, object After, int OldCost, int NewCost);
    internal readonly List<Change> Changes = new();
    private readonly List<object> _createdCards = new();
    private readonly object _runState;
    internal object NextRng { get; private set; }
    private bool _committed;

    private RefreshPlan(object runState) { _runState = runState; }

    internal static RefreshPlan Prepare(object inventory, bool refill, object localRng)
    {
        var player = GameApi.Player(inventory);
        var run = GameApi.Get(player, "RunState");
        var rngSet = GameApi.Get(player, "PlayerRng");
        var vanillaRng = GameApi.Call(rngSet, "ToSerializable");
        var allCards = new HashSet<object>(GameApi.Items(GameApi.Read(run, "_allCards")));
        var plan = new RefreshPlan(run);
        try
        {
            GameApi.Call(rngSet, "LoadFromSerializable", localRng);
            var shadow = Activator.CreateInstance(inventory.GetType(), player);
            foreach (var field in new[] { "_characterCardEntries", "_colorlessCardEntries", "_relicEntries", "_potionEntries" })
            {
                var copies = (IList)GameApi.Read(shadow, field);
                foreach (var entry in GameApi.Items(GameApi.Read(inventory, field)))
                {
                    var copy = GameApi.Clone(entry);
                    if (field.Contains("Card")) GameApi.Write(copy, "_inventory", shadow);
                    copies.Add(copy);
                }
            }

            var rng = GameApi.Get(rngSet, "Shops");
            plan.Relics(inventory, shadow, player, rng, refill);
            plan.Potions(inventory, shadow, player, rng, refill);
            plan.Cards(inventory, shadow, refill);
            if (plan.Changes.Count == 0) throw new InvalidOperationException("No eligible stocked slots to refresh.");
            plan.NextRng = GameApi.Call(rngSet, "ToSerializable");
            return plan;
        }
        catch
        {
            foreach (var card in GameApi.Items(GameApi.Read(run, "_allCards")).Where(c => !allCards.Contains(c)))
                GameApi.Call(run, "RemoveCard", card);
            throw;
        }
        finally
        {
            GameApi.Call(rngSet, "LoadFromSerializable", vanillaRng);
            plan._createdCards.AddRange(GameApi.Items(GameApi.Read(run, "_allCards")).Where(c => !allCards.Contains(c)));
        }
    }

    private void Relics(object inventory, object shadow, object player, object rng, bool refill)
    {
        var characterPool = GameApi.Get(GameApi.Get(player, "Character"), "RelicPool");
        var db = GameApi.Type("Models.ModelDb");
        var sharedType = GameApi.Type("Models.RelicPools.SharedRelicPool");
        var shared = db.GetMethod("RelicPool").MakeGenericMethod(sharedType).Invoke(null, null);
        var unlocks = GameApi.Get(player, "UnlockState");
        var pool = GameApi.Items(GameApi.Call(characterPool, "GetUnlockedRelics", unlocks))
            .Concat(GameApi.Items(GameApi.Call(shared, "GetUnlockedRelics", unlocks)))
            .Where(r => (bool)GameApi.Get(r, "IsAllowedInShops") && (bool)GameApi.Call(r, "IsAllowed", _runState)
                && !BlacklistBridge.IsBanned("Relic", r)).Distinct().ToArray();
        var used = GameApi.Items(GameApi.Get(player, "Relics")).Select(GameApi.Id).ToHashSet();
        var originals = GameApi.Items(GameApi.Get(inventory, "RelicEntries"));
        var copies = GameApi.Items(GameApi.Get(shadow, "RelicEntries"));
        for (int i = 0; i < copies.Length; i++)
        {
            var entry = copies[i];
            if (!refill && !(bool)GameApi.Get(entry, "IsStocked")) continue;
            // The third normal merchant slot remains Shop even after it has been purchased.
            var rarityType = GameApi.Property(GameApi.Type("Models.RelicModel"), "Rarity").PropertyType;
            var oldModel = GameApi.Get(entry, "Model");
            var shopSlot = i == 2 || (oldModel != null && GameApi.Get(oldModel, "Rarity").ToString() == "Shop");
            var rarity = shopSlot ? Enum.Parse(rarityType, "Shop")
                : GameApi.Call(GameApi.Type("Factories.RelicFactory"), "RollRarity", rng);
            var candidates = pool.Where(r => !used.Contains(GameApi.Id(r)) && GameApi.Get(r, "Rarity").Equals(rarity)).ToArray();
            if (candidates.Length == 0) throw new InvalidOperationException("No allowed relics for slot rarity " + rarity);
            var chosen = candidates[(int)GameApi.Call(rng, "NextInt", candidates.Length)];
            used.Add(GameApi.Id(chosen));
            GameApi.Set(entry, "Model", GameApi.Call(chosen, "ToMutable"));
            GameApi.Call(entry, "CalcCost");
            Add(originals[i], entry, "Model");
        }
    }

    private void Potions(object inventory, object shadow, object player, object rng, bool refill)
    {
        var pool = GameApi.Items(GameApi.PotionOptions(player))
            .Where(p => !BlacklistBridge.IsBanned("Potion", p)).Distinct().ToArray();
        var used = new HashSet<string>();
        var originals = GameApi.Items(GameApi.Get(inventory, "PotionEntries"));
        var copies = GameApi.Items(GameApi.Get(shadow, "PotionEntries"));
        for (int i = 0; i < copies.Length; i++)
        {
            var entry = copies[i];
            if (!refill && !(bool)GameApi.Get(entry, "IsStocked")) continue;
            var roll = (float)GameApi.Call(rng, "NextFloat", 1f);
            var rarity = roll <= .10f ? "Rare" : roll <= .35f ? "Uncommon" : "Common";
            var candidates = pool.Where(p => !used.Contains(GameApi.Id(p)) && GameApi.Get(p, "Rarity").ToString() == rarity).ToArray();
            // Filtering can empty a rarity. Choose another allowed rarity, never a banned potion.
            if (candidates.Length == 0) candidates = pool.Where(p => !used.Contains(GameApi.Id(p))).ToArray();
            if (candidates.Length == 0) throw new InvalidOperationException("No allowed potions for refresh.");
            var chosen = candidates[(int)GameApi.Call(rng, "NextInt", candidates.Length)];
            used.Add(GameApi.Id(chosen));
            GameApi.Set(entry, "Model", GameApi.Call(chosen, "ToMutable"));
            GameApi.Call(entry, "CalcCost");
            Add(originals[i], entry, "Model");
        }
    }

    private void Cards(object inventory, object shadow, bool refill)
    {
        foreach (var group in new[] { "CharacterCardEntries", "ColorlessCardEntries" })
        {
            var originals = GameApi.Items(GameApi.Get(inventory, group));
            var copies = GameApi.Items(GameApi.Get(shadow, group));
            for (int i = 0; i < copies.Length; i++)
            {
                var entry = copies[i];
                if (!refill && !(bool)GameApi.Get(entry, "IsStocked")) continue;
                // Filter the native pool before native rarity/type/upgrade/sale hooks run.
                var poolField = GameApi.Field(entry.GetType(), "_cardPool");
                var pool = GameApi.Items(poolField.GetValue(entry)).Where(c => !BlacklistBridge.IsBanned("Card", c)).ToArray();
                var array = Array.CreateInstance(GameApi.Type("Models.CardModel"), pool.Length);
                for (int j = 0; j < pool.Length; j++) array.SetValue(pool[j], j);
                poolField.SetValue(entry, array);
                GameApi.Call(entry, "Populate");
                var card = GameApi.Get(GameApi.Get(entry, "CreationResult"), "Card");
                if (BlacklistBridge.IsBanned("Card", card)) throw new InvalidOperationException("A card hook produced a blocked card.");
                Add(originals[i], entry, "CreationResult");
            }
        }
    }

    private void Add(object entry, object shadow, string property)
    {
        // Verify setters and backing cost before any real mutation.
        if (GameApi.Property(entry.GetType(), property).GetSetMethod(true) == null)
            throw new MissingMethodException(entry.GetType().Name, property + " setter");
        Changes.Add(new Change(entry, property, GameApi.Get(entry, property), GameApi.Get(shadow, property),
            (int)GameApi.Read(entry, "_cost"), (int)GameApi.Read(shadow, "_cost")));
    }
    internal void Apply()
    {
        foreach (var c in Changes) { GameApi.Set(c.Entry, c.Property, c.After); GameApi.Write(c.Entry, "_cost", c.NewCost); }
    }
    internal void Commit() { _committed = true; }
    internal void Rollback()
    {
        if (_committed) return;
        foreach (var c in Changes) { GameApi.Set(c.Entry, c.Property, c.Before); GameApi.Write(c.Entry, "_cost", c.OldCost); }
        foreach (var card in _createdCards) GameApi.Call(_runState, "RemoveCard", card);
    }
}
