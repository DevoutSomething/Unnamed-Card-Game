using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Game.Core.State
{

public class Sublane {
    public int PlayerId;                           
    public int MaxSize;                             
    public CardInstance[] Slots;                    
    public List<string> StatusEffects = new();      

    public Sublane(int playerId, int maxSize) {
        PlayerId = playerId;
        MaxSize = maxSize;
        Slots = new CardInstance[maxSize];
    }

    public bool HasEmptySlot => Slots.Any(s => s == null);
    public int FirstEmptySlot => Array.IndexOf(Slots, null);
    public int OccupiedCount => Slots.Count(s => s != null);

    // [JsonIgnore]: purely derived from Slots (already on the wire), and
    // dangerous to leave serializable — its runtime type is a private LINQ
    // iterator, not IEnumerable<CardInstance>, which TypeNameHandling.Auto
    // would try to stamp into "$type" and then fail to resolve on the way back.
    [JsonIgnore]
    public IEnumerable<CardInstance> Cards => Slots.Where(s => s != null);

    /// Turns a requested slot into an actual placeable slot index.
    /// requested >= 0: that exact slot if in range and empty, else -1.
    /// requested == -1: first empty slot, or -1 if full.
    public int ResolveSlot(int requested)
    {
        if (requested >= 0)
        {
            if (requested < Slots.Length && Slots[requested] == null) return requested;
            return -1;
        }
        return FirstEmptySlot;   // Array.IndexOf already returns -1 when full
    }

    public void Place(CardInstance card, int slot)
    {
        Slots[slot] = card;
    }

    public void RemoveAt(int slot) => Slots[slot] = null;


}
}