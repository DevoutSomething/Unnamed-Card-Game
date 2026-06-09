namespace Game.Core.State {
    public class GameState {
        public int Seed { get; }
        public int Tick { get; private set; } 
        public Player[] Players { get; }   
        public int ActivePlayerIndex { get; private set; }
        public Phase CurrentPhase { get; private set; }

        public Lane[] Lanes;
        public int SlotIndex { get; private set; }

        
        public GameState(int seed) {
            Seed = seed;
            Players = new[] { new Player(0), new Player(1) };
            CurrentPhase = Phase.P1Action;
            Lanes = new Lane[5];
            for (int i = 0; i < Lanes.Length; i++) {
                Lanes[i] = new Lane(position: i, slotsPerSide: 2);
            }
        }
    }

    public enum Phase { P1Action, P1Spells, P2Action, P2Spells, Combat, Event, Augment }
}