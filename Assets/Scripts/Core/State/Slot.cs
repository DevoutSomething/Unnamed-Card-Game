namespace Game.Core.State
{
    public enum SlotType { Action, Combat, Event, Shop, Augment }

    public record Slot(SlotType Type, int PlayerId = -1);
}