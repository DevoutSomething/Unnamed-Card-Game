namespace Game.Core.State
{
    public enum SlotType { Action, Combat, Event, Shop, Augment }

    public readonly struct Slot
    {
        public SlotType Type { get; }
        public int PlayerId { get; }   // -1 for system slots (Combat, Event, ...)

        public Slot(SlotType type, int playerId = -1)
        {
            Type = type;
            PlayerId = playerId;
        }
    }

    public static class Rotation
    {
        public static readonly Slot[] Slots =
        {
            new(SlotType.Action, 0), new(SlotType.Action, 1),
            new(SlotType.Action, 1), new(SlotType.Action, 0),
            new(SlotType.Combat),
            new(SlotType.Action, 1), new(SlotType.Action, 0),
            new(SlotType.Action, 0), new(SlotType.Action, 1),
            new(SlotType.Combat),
            new(SlotType.Event),
        };

        public static int Length => Slots.Length;
    }
}