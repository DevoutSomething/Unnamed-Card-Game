namespace Game.Core.State
{
    public static class Rotation
    {
        public static readonly Slot[] Sequence = new[]
        {
            new Slot(SlotType.Action, 0),  // P1
            new Slot(SlotType.Action, 1),  // P2
            new Slot(SlotType.Action, 1),  // P2
            new Slot(SlotType.Action, 0),  // P1
            new Slot(SlotType.Combat),
            new Slot(SlotType.Action, 1),  // P2
            new Slot(SlotType.Action, 0),  // P1
            new Slot(SlotType.Action, 0),  // P1
            new Slot(SlotType.Action, 1),  // P2
            new Slot(SlotType.Combat),
            new Slot(SlotType.Event),
        };

        public static int Length => Sequence.Length;
    }
}