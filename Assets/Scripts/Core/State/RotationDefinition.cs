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
            // The rotation's tail: pick an augment, then shop. Later the Event
            // slot joins these two in some order.
            new(SlotType.Augment),
            new(SlotType.Shop),
        };

        public static int Length => Slots.Length;

        /// <summary>
        /// True for a player's first action slot in each stretch between system
        /// slots (Combat/Event/...); false for that player's second (or later)
        /// action slot in the same stretch. Guy cards are only playable on a
        /// "main" slot — a player's other slots in the stretch are spell-only.
        /// Derived once from the static rotation table, since it never changes
        /// between cycles (indices repeat identically every RotationIndex).
        /// </summary>
        public static readonly bool[] IsMainActionSlot = ComputeMainActionSlots();

        static bool[] ComputeMainActionSlots()
        {
            var result = new bool[Slots.Length];
            var seenThisStretch = new bool[2];

            for (int i = 0; i < Slots.Length; i++)
            {
                var slot = Slots[i];
                if (slot.Type != SlotType.Action)
                {
                    seenThisStretch[0] = seenThisStretch[1] = false;
                    continue;
                }

                result[i] = !seenThisStretch[slot.PlayerId];
                seenThisStretch[slot.PlayerId] = true;
            }

            return result;
        }
    }
}