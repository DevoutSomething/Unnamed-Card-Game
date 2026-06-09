using System;
using System.Collections.Generic;
using System.Linq;

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
    public IEnumerable<CardInstance> Cards => Slots.Where(s => s != null);
}
}