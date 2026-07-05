namespace Game.Core.State
{
    public class GameState
    {
        public int Seed { get; }
        public Player[] Players { get; }
        public Lane[] Lanes { get; }

        // Rotation pointer. SlotIndex is the single source of truth for
        // "where are we in the turn structure" — everything else is derived.
        public int SlotIndex { get; set; }
        public int RotationIndex { get; set; }

        public SlotType CurrentSlotType => Rotation.Slots[SlotIndex].Type;
        public int ActivePlayerId => Rotation.Slots[SlotIndex].PlayerId;

        public bool IsGameOver =>
            Players[0].Health <= 0 || Players[1].Health <= 0;

        public Game.Core.Util.GameRng Rng { get; }

        public int NextCardInstanceId { get; set; } = 1;

        public GameState(int seed, int laneCount = 5, int slotsPerSide = 2)
        {
            Seed = seed;
            Rng = new Game.Core.Util.GameRng(seed);
            Players = new[] { new Player(0), new Player(1) };

            Lanes = new Lane[laneCount];
            for (int i = 0; i < Lanes.Length; i++)
            {
                Lanes[i] = new Lane(position: i, slotsPerSide: slotsPerSide);
            }

            // SlotIndex = 0 by default, which is P1's first action slot
            // per the Rotation table. RotationIndex = 0. Nothing else to do —
            // HandleStartGame sets these explicitly anyway.
        }
    }
}